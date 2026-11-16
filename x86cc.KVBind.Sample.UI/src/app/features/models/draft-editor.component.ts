import { CommonModule, NgComponentOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ChangeTreeTableComponent } from '../../shared/components/change-tree-table.component';
import { UserService } from '../../core/services/user.service';
import { SchemaFieldComponent } from './schema-field.component';
import { OperationContext, OperationRenderer, OperationRendererRegistry } from './operations/operation-renderers';
import { AggregateDraft, ModelApiService, PatchOperation, ValidationResult, errorText } from './model-api.service';
import {
  DataObject, ID_KEY, KVCollectionSchema, KVFieldSchema, KVItemTypeSchema, KVNestedNodeSchema, KVNodeSchema, KVSchemaDocument,
  Located, TYPE_KEY, UI_ROLE, allows, annotation, hasReadOnlyMembers, joinPath, labelOf, locate, titleField, typeLabel, valueAt,
} from './schema';

// The draft editor for any model. Everything on screen comes from the schema document and the projected
// data: fields, groups, collections and nested nodes are walked recursively, so the same component edits a
// three-field claim and a thousand-field application. Edits are batched into one patch (sent on navigation,
// on "Save", or every 10 s); structural actions (ADD, REMOVE, MOVE, RESET, INIT, DROP, custom operations)
// are sent at once. What a member allows — read-only, inherited, allowed operations — is read from the schema.
@Component({
  selector: 'app-draft-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, NgComponentOutlet, ChangeTreeTableComponent, SchemaFieldComponent],
  templateUrl: './draft-editor.component.html',
})
export class DraftEditorComponent implements OnInit, OnDestroy {
  model = '';
  id = '';
  draftId = '';
  schema: KVSchemaDocument | null = null;
  draft: AggregateDraft | null = null;
  error = '';
  saving = false;
  validating = false;
  validation: ValidationResult | null = null;
  errorsByPath: Record<string, string[]> = {};
  overlayOpen = false;
  eventsOpen = false;

  // The path being edited and how we got there (breadcrumb).
  nav: string[] = [''];

  // Field edits not yet sent, by path.
  pending = new Map<string, PatchOperation>();
  pendingValues: Record<string, unknown> = {};
  autoSaveLabel = '';

  // One context per rendered custom operation, keyed by "<path>|<operation>", so renderers keep their state.
  private operationContexts = new Map<string, { inputs: { context: OperationContext } }>();

  private timer?: ReturnType<typeof setInterval>;
  private changedExact = new Set<string>();
  private changedWithPrefixes = new Set<string>();

  constructor(
    private readonly api: ModelApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly users: UserService,
    private readonly operationRenderers: OperationRendererRegistry,
  ) {}

  ngOnInit(): void {
    this.model = this.route.snapshot.paramMap.get('model') ?? '';
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    this.draftId = this.route.snapshot.paramMap.get('draftId') ?? '';
    const at = this.route.snapshot.queryParamMap.get('at');
    if (at) this.nav = ['', ...prefixes(at)];

    this.api.definition(this.model).subscribe({ next: schema => (this.schema = schema), error: e => (this.error = errorText(e)) });
    this.load();
    this.timer = setInterval(() => this.autoSave(), 10_000);
  }

  ngOnDestroy(): void { if (this.timer) clearInterval(this.timer); }

  // ── Location ────────────────────────────────────────────────────────────────

  get path(): string { return this.nav[this.nav.length - 1]; }

  get here(): Located | null {
    return this.schema && this.draft ? locate(this.schema, this.draft.data, this.path) : null;
  }

  crumbLabel(path: string): string {
    if (!this.schema || !this.draft) return path;
    return locate(this.schema, this.draft.data, path)?.label ?? path;
  }

  open(path: string): void { this.flush(); this.nav = [...this.nav, path]; }
  goTo(index: number): void { this.flush(); this.nav = this.nav.slice(0, index + 1); }
  back(): void { this.flush(); if (this.nav.length > 1) this.nav = this.nav.slice(0, -1); }

  // Jumps to the node holding a path (e.g. a validation error), building the breadcrumb on the way.
  reveal(path: string): void {
    if (!this.schema || !this.draft) return;
    // Every prefix that is a node, collection or nested slot is a level; a field segment is not.
    const chain = prefixes(path).filter(p => locate(this.schema!, this.draft!.data, p) != null);
    this.flush();
    this.nav = ['', ...chain];
  }

  // ── Values ──────────────────────────────────────────────────────────────────

  valueOf(path: string): unknown {
    return path in this.pendingValues ? this.pendingValues[path] : valueAt(this.draft?.data, path);
  }

  isInherited(path: string): boolean { return (this.draft?.inheritedPaths ?? []).some(p => under(path, p)); }
  // A path is changed when it, one of its ancestors, or something beneath it changed. The set holds every
  // changed path and all its prefixes, so the check is a few lookups instead of a scan per field.
  isChanged(path: string): boolean {
    if (this.changedWithPrefixes.has(path)) return true;
    for (const prefix of prefixes(path)) if (this.changedExact.has(prefix)) return true;
    return false;
  }

  // Collections show a page of items at a time, so a thousand-line collection stays responsive.
  readonly pageSize = 50;
  pages: Record<string, number> = {};
  visibleItems(path: string, items: DataObject[]): DataObject[] { return items.slice(0, (this.pages[path] ?? 1) * this.pageSize); }
  showMore(path: string): void { this.pages[path] = (this.pages[path] ?? 1) + 1; }
  errorsFor(path: string): string[] { return this.errorsByPath[path] ?? []; }

  errorCountUnder(path: string): number {
    return Object.entries(this.errorsByPath).filter(([p]) => under(p, path)).reduce((n, [, list]) => n + list.length, 0);
  }

  fieldPath(nodePath: string, field: KVFieldSchema): string { return joinPath(nodePath, field.key); }

  wide(field: KVFieldSchema): boolean {
    const control = annotation(field.annotations, 'ui:control');
    return control === 'textarea' || control === 'radio' || control === 'multiselect'
      || !!field.allowedValues?.appliesToElements || field.isFlags;
  }

  isSection(group: KVNodeSchema): boolean { return annotation(group.annotations, UI_ROLE) === 'section'; }

  onField(path: string, value: unknown): void {
    this.pendingValues[path] = value;
    this.pending.set(path, value == null ? { operationCode: 'UNSET', path: `/${path}` } : { operationCode: 'SET', path: `/${path}`, value });
  }

  // ── Summaries for the cards ─────────────────────────────────────────────────

  groupSummary(nodePath: string, group: KVNodeSchema): string {
    const values = group.fields
      .map(f => this.valueOf(joinPath(nodePath, f.key)))
      .filter(v => v != null && v !== '' && !(Array.isArray(v) && v.length === 0))
      .map(v => (Array.isArray(v) ? v.join(', ') : String(v)));
    return values.length ? values.slice(0, 3).join(' · ') : 'Not set';
  }

  itemsOf(nodePath: string, collection: KVCollectionSchema): DataObject[] {
    return (valueAt(this.draft?.data, joinPath(nodePath, collection.key)) as DataObject[]) ?? [];
  }

  countRule(collection: KVCollectionSchema): string {
    const parts: string[] = [];
    if (collection.notEmpty) parts.push('not empty');
    if (collection.minCount != null) parts.push(`min ${collection.minCount}`);
    if (collection.maxCount != null) parts.push(`max ${collection.maxCount}`);
    for (const rule of collection.aggregateRules) parts.push(`Σ ${rule.fieldKey} ${comparisonSymbol(rule.comparison)} ${rule.threshold}`);
    return parts.join(' · ');
  }

  nestedSummary(nodePath: string, nested: KVNestedNodeSchema): string {
    const value = valueAt(this.draft?.data, joinPath(nodePath, nested.key)) as DataObject | null;
    if (!value) return nested.isRequired ? 'Required — not set' : 'Not set';
    const type = nested.types.find(t => t.token === value[TYPE_KEY]);
    const title = type ? titleField(type.node) : null;
    const name = title ? value[title.key] : null;
    return [type ? typeLabel(type) : String(value[TYPE_KEY]), name].filter(v => v != null && v !== '').join(' · ');
  }

  itemTitle(collection: KVCollectionSchema, item: DataObject, index: number): string {
    const type = collection.itemTypes.find(t => t.token === item[TYPE_KEY]);
    const title = type ? titleField(type.node) : null;
    const name = title ? item[title.key] : null;
    return name != null && name !== '' ? String(name) : `${type ? typeLabel(type) : 'Item'} #${index + 1}`;
  }

  itemTypeOf(collection: KVCollectionSchema, item: DataObject): KVItemTypeSchema | undefined {
    return collection.itemTypes.find(t => t.token === item[TYPE_KEY]);
  }

  activeType(nested: KVNestedNodeSchema, value: DataObject | null): KVItemTypeSchema | undefined {
    return value ? nested.types.find(t => t.token === value[TYPE_KEY]) : undefined;
  }

  allows = allows;

  canReset(collection: KVCollectionSchema, item: DataObject): boolean {
    const type = this.itemTypeOf(collection, item);
    return allows(collection, 'RESET') && !!type && !hasReadOnlyMembers(type.node);
  }

  hasReadOnly(node: KVNodeSchema): boolean { return hasReadOnlyMembers(node); }

  show(value: unknown): string { return value == null || value === '' ? '—' : String(value); }
  labelOf = labelOf;
  typeLabel = typeLabel;
  joinPath = joinPath;
  idOf(item: DataObject): string { return String(item[ID_KEY]); }

  // ── Structural actions ──────────────────────────────────────────────────────

  addItem(collectionPath: string, collection: KVCollectionSchema, type: KVItemTypeSchema): void {
    const itemId = crypto.randomUUID();
    const value = collection.itemTypes.length > 1 ? { itemId, typeToken: type.token } : { itemId };
    this.send([{ operationCode: 'ADD', path: `/${collectionPath}`, value }], () => this.open(joinPath(collectionPath, itemId)));
  }

  removeItem(itemPath: string): void {
    this.send([{ operationCode: 'REMOVE', path: `/${itemPath}` }], () => {
      if (this.path === itemPath) this.nav = this.nav.slice(0, -1);
    });
  }

  moveItem(itemPath: string, toIndex: number): void { this.send([{ operationCode: 'MOVE', path: `/${itemPath}`, value: toIndex }]); }

  resetNode(path: string): void { this.send([{ operationCode: 'RESET', path: `/${path}` }]); }

  discardPath(path: string): void { this.send([{ operationCode: 'DISCARD', path: `/${path}` }]); }

  // INIT also switches an initialized slot to another subtype, starting it fresh.
  initNested(slotPath: string, token: string): void {
    this.send([{ operationCode: 'INIT', path: `/${slotPath}`, value: token }]);
  }

  dropNested(slotPath: string): void { this.send([{ operationCode: 'DROP', path: `/${slotPath}` }]); }

  // Custom operations the UI can render: those with a registered renderer (see operations/sample-operations.ts).
  // An operation without one is not shown.
  renderableOperations(ops: string[], path: string): Array<{ op: string; renderer: OperationRenderer; inputs: { context: OperationContext } }> {
    const result = [];
    for (const op of ops) {
      const renderer = this.operationRenderers.find(this.model, op);
      if (renderer) result.push({ op, renderer, inputs: this.operationInputs(path, op, renderer) });
    }
    return result;
  }

  runOperation(path: string, operation: string, payload?: unknown): void {
    this.send([{ operationCode: operation, path: path ? `/${path}` : '/', value: payload }]);
  }

  private operationInputs(path: string, operation: string, renderer: OperationRenderer): { context: OperationContext } {
    const key = `${path}|${operation}`;
    let entry = this.operationContexts.get(key);
    if (!entry) {
      const editor = this;
      const context: OperationContext = {
        model: this.model,
        operation,
        path,
        get data() { return valueAt(editor.draft?.data, path); },
        get busy() { return editor.saving; },
        options: renderer.options ?? {},
        run: (payload?: unknown) => editor.runOperation(path, operation, payload),
      };
      entry = { inputs: { context } };
      this.operationContexts.set(key, entry);
    }
    return entry.inputs;
  }

  // ── Save / validate / commit ────────────────────────────────────────────────

  get pendingCount(): number { return this.pending.size; }

  saveNow(): void { this.flush(); }

  validate(): void {
    this.flush(() => {
      this.validating = true;
      this.api.validate(this.model, this.id, this.draftId).subscribe({
        next: result => { this.validating = false; this.applyValidation(result); },
        error: e => { this.validating = false; this.error = `Validation failed: ${errorText(e)}`; },
      });
    });
  }

  review(): void { this.flush(() => this.router.navigate(['/m', this.model, this.id, 'drafts', this.draftId, 'review'])); }

  discardAll(): void {
    this.pending.clear();
    this.pendingValues = {};
    this.api.reset(this.model, this.id, this.draftId).subscribe({ next: d => this.apply(d), error: e => (this.error = errorText(e)) });
  }

  rebaseNow(): void {
    this.flush(() => this.api.beginRebase(this.model, this.id, this.draftId).subscribe({
      next: result => {
        if (result.outcome !== 'AlreadyCurrent') this.router.navigate(['/m', this.model, this.id, 'drafts', this.draftId, 'rebase']);
        else this.load();
      },
      error: e => (this.error = `Unable to rebase. ${errorText(e)}`),
    }));
  }

  computedEntries(): Array<[string, unknown]> { return Object.entries(this.draft?.computed ?? {}); }

  eventPayload(payload: string): string {
    try { return JSON.stringify(JSON.parse(payload)); } catch { return payload; }
  }

  // ── Plumbing ────────────────────────────────────────────────────────────────

  private load(): void {
    this.api.draft(this.model, this.id, this.draftId).subscribe({
      next: draft => this.apply(draft),
      error: e => (this.error = `Unable to load draft. ${errorText(e)}`),
    });
  }

  private autoSave(): void {
    if (this.pending.size === 0 || this.saving) return;
    this.autoSaveLabel = 'Auto-saving…';
    this.flush(() => { this.autoSaveLabel = 'Saved'; setTimeout(() => (this.autoSaveLabel = ''), 2000); });
  }

  // Sends the pending field edits (if any), then runs `then`.
  private flush(then?: () => void): void {
    if (this.pending.size === 0) { then?.(); return; }
    const ops = [...this.pending.values()];
    this.send(ops, then, () => { this.pending.clear(); this.pendingValues = {}; });
  }

  // Sends operations, pending field edits first so they are never overtaken by a structural change.
  private send(ops: PatchOperation[], then?: () => void, sent?: () => void): void {
    const pendingOps = ops.some(op => this.pending.get(op.path.slice(1)) === op) ? [] : [...this.pending.values()];
    const all = [...pendingOps, ...ops];
    this.saving = true;
    this.error = '';
    this.api.patch(this.model, this.id, this.draftId, all).subscribe({
      next: draft => {
        this.saving = false;
        this.pending.clear();
        this.pendingValues = {};
        sent?.();
        this.apply(draft);
        if (this.validation) this.revalidateQuietly();
        then?.();
      },
      error: (e: HttpErrorResponse) => {
        this.saving = false;
        this.error = `Patch rejected: ${errorText(e)}`;
      },
    });
  }

  private revalidateQuietly(): void {
    this.api.validate(this.model, this.id, this.draftId).subscribe({ next: r => this.applyValidation(r) });
  }

  private applyValidation(result: ValidationResult): void {
    this.validation = result;
    this.errorsByPath = {};
    for (const err of result.errors) (this.errorsByPath[err.path] ??= []).push(`${err.message} (${err.code})`);
  }

  private apply(draft: AggregateDraft): void {
    if (draft.isRebasing) {
      this.router.navigate(['/m', this.model, this.id, 'drafts', this.draftId, 'rebase']);
      return;
    }
    this.draft = draft;
    this.changedExact = new Set(draft.changedPaths);
    this.changedWithPrefixes = new Set(draft.changedPaths.flatMap(p => prefixes(p)));

    // A structural change can remove the node being edited (e.g. its item was removed): step back to
    // the nearest level that still exists.
    if (this.schema) {
      while (this.nav.length > 1 && !locate(this.schema, draft.data, this.path)) this.nav = this.nav.slice(0, -1);
    }
  }
}

function under(path: string, ancestor: string): boolean {
  return ancestor === '' || path === ancestor || path.startsWith(`${ancestor}/`);
}

// "a/b/c" -> ["a", "a/b", "a/b/c"]
function prefixes(path: string): string[] {
  const segments = path.split('/').filter(s => s.length > 0);
  return segments.map((_, i) => segments.slice(0, i + 1).join('/'));
}

function comparisonSymbol(comparison: string): string {
  return ({ LessThan: '<', LessThanOrEqual: '≤', GreaterThan: '>', GreaterThanOrEqual: '≥' } as Record<string, string>)[comparison] ?? comparison;
}
