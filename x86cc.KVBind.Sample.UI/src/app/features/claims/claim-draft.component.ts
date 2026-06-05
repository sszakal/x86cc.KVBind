import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ChangeTreeTableComponent } from './change-tree-table.component';
import {
  ClaimApiService,
  ClaimDraftResponse,
  ClaimPatchOperationRequest,
  CollectionItemTypeMeta,
  CollectionMeta,
  DefinitionSchemaResponse,
  FieldGroupMeta,
  NestedNodeMeta,
  StaleDraftResponse,
  ValidateDraftResponse,
} from './claim-api.service';
import { UserService } from '../../core/services/user.service';
import { FieldInputComponent, FieldMeta } from '../../shared/components/field-input.component';

// A location in the claim tree the user is currently editing.
type NavFrame =
  | { kind: 'root' }
  | { kind: 'group'; key: string }
  | { kind: 'collection'; key: string }
  | { kind: 'item'; collKey: string; itemId: string }
  | { kind: 'nested'; key: string };

@Component({
  selector: 'app-claim-draft',
  imports: [CommonModule, FormsModule, RouterModule, ChangeTreeTableComponent, FieldInputComponent],
  templateUrl: './claim-draft.component.html',
})
export class ClaimDraftComponent implements OnInit, OnDestroy {
  claimId = '';
  draftId = '';
  draft: ClaimDraftResponse | null = null;
  definition: DefinitionSchemaResponse | null = null;
  error = '';
  staleDraft: StaleDraftResponse | null = null;
  saving = false;
  committing = false;
  validating = false;
  overlayOpen = false;
  validationResult: ValidateDraftResponse | null = null;
  isStale = false;

  // Flat map of current field values: { path -> value }
  // path is the canonical KVBind path, e.g. "Status", "Policy/CoverageType"
  fieldValues: Record<string, unknown> = {};

  // Pending patch operations — accumulated between auto-patch cycles
  pendingOps: Map<string, ClaimPatchOperationRequest> = new Map();
  autoSaveLabel = '';

  // Collection add-item forms
  newItemValues: Record<string, Record<string, unknown>> = {};

  // Expanded collection item IDs
  expandedItems = new Set<string>();

  // Nested node
  nestedNodeType: Record<string, string | null> = {};
  nestedNodeValues: Record<string, Record<string, unknown>> = {};

  // Hierarchical navigation — a stack of frames; the last is the level being edited.
  navStack: NavFrame[] = [{ kind: 'root' }];

  private autoSaveTimer?: ReturnType<typeof setInterval>;

  constructor(
    private readonly api: ClaimApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly userService: UserService,
  ) {}

  ngOnInit(): void {
    this.claimId = this.route.snapshot.paramMap.get('claimId') ?? '';
    this.draftId = this.route.snapshot.paramMap.get('draftId') ?? '';

    this.api.getDefinition().subscribe({ next: def => { this.definition = def; this.initNewItemForms(); } });
    this.load();

    // Auto-patch every 10 seconds if there are pending changes
    this.autoSaveTimer = setInterval(() => this.autoPatch(), 10_000);
  }

  ngOnDestroy(): void {
    if (this.autoSaveTimer) clearInterval(this.autoSaveTimer);
  }

  get username(): string { return this.userService.getUser() ?? 'unknown'; }
  get pendingCount(): number { return this.pendingOps.size; }

  // ── Hierarchical navigation ──

  get currentFrame(): NavFrame { return this.navStack[this.navStack.length - 1]; }
  get currentItemId(): string { const f = this.currentFrame; return f.kind === 'item' ? f.itemId : ''; }

  private drillTo(frame: NavFrame): void { this.flushPending(); this.navStack = [...this.navStack, frame]; }
  back(): void { this.flushPending(); if (this.navStack.length > 1) this.navStack = this.navStack.slice(0, -1); }
  goToFrame(index: number): void { this.flushPending(); this.navStack = this.navStack.slice(0, index + 1); }

  openGroup(key: string): void { this.drillTo({ kind: 'group', key }); }
  openCollection(key: string): void { this.drillTo({ kind: 'collection', key }); }
  openNested(key: string): void { this.drillTo({ kind: 'nested', key }); }
  openItem(collKey: string, itemId: string): void { this.drillTo({ kind: 'item', collKey, itemId }); }

  // Flush pending edits so each level change persists (back-navigation commits the section's edits).
  private flushPending(): void { if (this.pendingOps.size > 0) this.saveNow(); }

  // Definition meta resolved for the current frame.
  get currentGroup(): FieldGroupMeta | null {
    const f = this.currentFrame;
    return f.kind === 'group' ? this.definition?.fieldGroups.find(g => g.key === f.key) ?? null : null;
  }
  get currentCollection(): CollectionMeta | null {
    const f = this.currentFrame;
    const key = f.kind === 'collection' ? f.key : f.kind === 'item' ? f.collKey : null;
    return key ? this.definition?.collections.find(c => c.key === key) ?? null : null;
  }
  get currentItemType(): CollectionItemTypeMeta | null {
    const coll = this.currentCollection;
    return coll && coll.itemTypes.length > 0 ? coll.itemTypes[0] : null;
  }
  get currentNested(): NestedNodeMeta | null {
    const f = this.currentFrame;
    return f.kind === 'nested' ? this.definition?.nestedNodes.find(n => n.key === f.key) ?? null : null;
  }

  frameLabel(frame: NavFrame): string {
    switch (frame.kind) {
      case 'root': return 'Claim';
      case 'group': return this.definition?.fieldGroups.find(g => g.key === frame.key)?.label ?? frame.key;
      case 'collection': return this.definition?.collections.find(c => c.key === frame.key)?.label ?? frame.key;
      case 'nested': return this.definition?.nestedNodes.find(n => n.key === frame.key)?.label ?? frame.key;
      case 'item': {
        const idx = this.getItemsForCollection(frame.collKey).findIndex(i => i.itemId === frame.itemId);
        return idx >= 0 ? `Item ${idx + 1}` : 'Item';
      }
    }
  }

  // Root-card summaries.
  groupSummaryText(group: FieldGroupMeta): string {
    const vals = group.fields
      .map(f => this.fieldValues[`${group.key}/${f.key}`])
      .filter(v => v != null && v !== '');
    return vals.length ? vals.slice(0, 2).map(v => String(v)).join(' · ') : 'Not set';
  }
  nestedSummaryText(): string {
    const c = this.draft?.claim.claimant;
    return c ? `${c.displayName ?? '—'} · ${c.type}` : 'Not set';
  }

  // Create an empty collection item, then drill straight into editing it.
  addAndOpenItem(collKey: string): void {
    const itemId = crypto.randomUUID();
    this.dispatchPatch([{ operationCode: 'ADD', path: `/${collKey}`, value: { itemId } }], () => {
      this.navStack = [...this.navStack, { kind: 'item', collKey, itemId }];
    });
  }

  // Delete the current item and pop back to the collection.
  removeItemAndBack(collKey: string, itemId: string): void {
    this.expandedItems.delete(itemId);
    this.dispatchPatch([{ operationCode: 'REMOVE', path: `/${collKey}/${itemId}` }], () => {
      this.navStack = this.navStack.slice(0, -1);
    });
  }

  load(): void {
    this.api.getDraft(this.claimId, this.draftId).subscribe({
      next: draft => this.applyDraft(draft),
      error: err => (this.error = `Unable to load draft. ${err.message ?? ''}`),
    });
  }

  // Called by FieldInputComponent whenever a field value changes
  onFieldChange(path: string, value: unknown): void {
    this.fieldValues[path] = value;
    this.pendingOps.set(path, { operationCode: value == null ? 'UNSET' : 'SET', path: `/${path}`, value: value ?? undefined });
  }

  // Flush pending ops immediately (manual save)
  saveNow(): void {
    if (this.pendingOps.size === 0) return;
    this.dispatchPatch([...this.pendingOps.values()], () => {
      this.pendingOps.clear();
      this.autoSaveLabel = '';
    });
  }

  // Auto-patch timer handler
  private autoPatch(): void {
    if (this.pendingOps.size === 0) return;
    this.autoSaveLabel = 'Auto-saving…';
    this.dispatchPatch([...this.pendingOps.values()], () => {
      this.pendingOps.clear();
      this.autoSaveLabel = 'Saved';
      setTimeout(() => { this.autoSaveLabel = ''; }, 2000);
    });
  }

  // ── COLLECTIONS ──

  getNewItemField(collKey: string, fieldKey: string): unknown {
    return this.newItemValues[collKey]?.[fieldKey] ?? null;
  }

  setNewItemField(collKey: string, fieldKey: string, value: unknown): void {
    if (!this.newItemValues[collKey]) this.newItemValues[collKey] = {};
    this.newItemValues[collKey][fieldKey] = value;
  }

  addItem(collKey: string, typeToken: string, fields: FieldMeta[]): void {
    const itemId = crypto.randomUUID();
    const ops: ClaimPatchOperationRequest[] = [
      { operationCode: 'ADD', path: `/${collKey}`, value: { itemId } },
    ];
    const vals = this.newItemValues[collKey] ?? {};
    for (const field of fields) {
      const val = vals[field.key];
      if (val != null && val !== '') {
        ops.push({ operationCode: 'SET', path: `/${collKey}/${itemId}/${field.key}`, value: val });
      }
    }
    this.dispatchPatch(ops, () => {
      this.newItemValues[collKey] = {};
      this.expandedItems.add(itemId);
    });
  }

  removeItem(collKey: string, itemId: string): void {
    this.expandedItems.delete(itemId);
    this.dispatchPatch([{ operationCode: 'REMOVE', path: `/${collKey}/${itemId}` }]);
  }

  toggleItem(itemId: string): void {
    if (this.expandedItems.has(itemId)) this.expandedItems.delete(itemId);
    else this.expandedItems.add(itemId);
  }

  getItemField(collKey: string, itemId: string, fieldKey: string): unknown {
    return this.fieldValues[`${collKey}/${itemId}/${fieldKey}`] ?? null;
  }

  onItemFieldChange(collKey: string, itemId: string, fieldKey: string, value: unknown): void {
    const path = `${collKey}/${itemId}/${fieldKey}`;
    this.fieldValues[path] = value;
    this.pendingOps.set(path, { operationCode: value == null ? 'UNSET' : 'SET', path: `/${path}`, value: value ?? undefined });
  }

  // ── NESTED NODES ──

  setNestedType(nodeKey: string, typeToken: string): void {
    this.nestedNodeType[nodeKey] = typeToken;
  }

  getNestedNodeField(nodeKey: string, fieldKey: string): unknown {
    return this.nestedNodeValues[nodeKey]?.[fieldKey] ?? null;
  }

  onNestedNodeFieldChange(nodeKey: string, fieldKey: string, value: unknown): void {
    if (!this.nestedNodeValues[nodeKey]) this.nestedNodeValues[nodeKey] = {};
    this.nestedNodeValues[nodeKey][fieldKey] = value;
  }

  saveNestedNode(nodeKey: string, typeToken: string, fields: FieldMeta[]): void {
    const ops: ClaimPatchOperationRequest[] = [
      { operationCode: 'INIT', path: `/${nodeKey}`, value: typeToken },
    ];
    const vals = this.nestedNodeValues[nodeKey] ?? {};
    const fieldPath = (f: FieldMeta) => `/${nodeKey}/${f.key}`;
    for (const field of fields) {
      const val = vals[field.key];
      ops.push({ operationCode: val != null ? 'SET' : 'UNSET', path: fieldPath(field), value: val ?? undefined });
    }
    this.dispatchPatch(ops);
  }

  dropNestedNode(nodeKey: string): void {
    this.nestedNodeType[nodeKey] = null;
    this.nestedNodeValues[nodeKey] = {};
    this.dispatchPatch([{ operationCode: 'DROP', path: `/${nodeKey}` }]);
  }

  // ── COMMIT ──

  validate(): void {
    this.validating = true;
    this.api.validateDraft(this.claimId, this.draftId).subscribe({
      next: result => {
        this.validationResult = result;
        this.validating = false;
      },
      error: err => {
        this.error = `Validation failed: ${err.message ?? ''}`;
        this.validating = false;
      },
    });
  }

  // Commit goes through the review screen: flush pending edits, then navigate to review.
  commit(): void {
    if (this.pendingOps.size > 0) {
      this.dispatchPatch([...this.pendingOps.values()], () => {
        this.pendingOps.clear();
        this.goToReview();
      });
    } else {
      this.goToReview();
    }
  }

  private goToReview(): void {
    this.router.navigate(['/claims', this.claimId, 'drafts', this.draftId, 'review']);
  }

  // Stale banner action — rebase without committing.
  rebaseNow(): void {
    this.error = '';
    this.api.beginRebase(this.claimId, this.draftId).subscribe({
      next: result => {
        if (result.outcome === 'ConflictsPending') {
          this.router.navigate(['/claims', this.claimId, 'drafts', this.draftId, 'rebase']);
        } else {
          this.isStale = false;
          this.load();
        }
      },
      error: err => (this.error = `Unable to rebase. ${err.message ?? ''}`),
    });
  }

  // ── HELPERS ──

  groupFields(group: FieldGroupMeta): FieldMeta[] { return group.fields; }

  label(v: string): string {
    return v.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
  }

  priorityColor(p: string | null): string {
    return ({ critical: 'bg-red-100 text-red-700 dark:bg-red-950/40 dark:text-red-300', high: 'bg-orange-100 text-orange-700', medium: 'bg-yellow-100 text-yellow-700', low: 'bg-blue-100 text-blue-700' } as Record<string, string>)[p ?? ''] ?? 'bg-gray-100 text-gray-500';
  }

  statusColor(s: string | null): string {
    return ({ approved: 'bg-emerald-100 text-emerald-700', in_review: 'bg-blue-100 text-blue-700', rejected: 'bg-red-100 text-red-700', closed: 'bg-gray-100 text-gray-500' } as Record<string, string>)[s ?? ''] ?? 'bg-yellow-100 text-yellow-700';
  }

  getItemsForCollection(collKey: string): { itemId: string }[] {
    if (collKey === 'DamagedItems') return this.draft?.claim.damagedItems ?? [];
    if (collKey === 'Notes') return this.draft?.claim.notes ?? [];
    return [];
  }

  getItemSummary(collKey: string, itemId: string): string {
    if (collKey === 'DamagedItems') {
      const item = this.draft?.claim.damagedItems.find(i => i.itemId === itemId);
      return item ? `${item.description ?? '—'} · ${item.estimatedAmount?.toLocaleString('en-US', { style: 'currency', currency: 'USD' })}` : itemId.slice(0, 8);
    }
    if (collKey === 'Notes') {
      const note = this.draft?.claim.notes.find(n => n.itemId === itemId);
      return note?.text ?? '—';
    }
    return itemId.slice(0, 8);
  }

  private dispatchPatch(ops: ClaimPatchOperationRequest[], onSuccess?: () => void): void {
    this.saving = true;
    this.error = '';
    this.api.patchDraft(this.claimId, this.draftId, ops).subscribe({
      next: draft => {
        this.applyDraft(draft);
        this.saving = false;
        onSuccess?.();
      },
      error: err => {
        this.error = `Patch failed: ${err.message ?? ''}`;
        this.saving = false;
      },
    });
  }

  private applyDraft(draft: ClaimDraftResponse): void {
    // Forced merge: if a rebase is in progress, the editor is locked behind the merge screen.
    if (draft.isRebasing) {
      this.router.navigate(['/claims', this.claimId, 'drafts', this.draftId, 'rebase']);
      return;
    }
    this.isStale = draft.isStale;
    this.draft = draft;
    // Sync flat field values from draft (only fields not currently pending edits)
    const pending = new Set(this.pendingOps.keys());
    const set = (path: string, val: unknown) => { if (!pending.has(path)) this.fieldValues[path] = val; };

    set('ClaimNumber', draft.claim.claimNumber);
    set('Status', draft.claim.status);
    set('Priority', draft.claim.priority);
    set('IncidentDate', draft.claim.incidentDate);
    set('Description', draft.claim.description);
    set('Tags', draft.claim.tags);
    set('Policy/PolicyNumber', draft.claim.policy.policyNumber);
    set('Policy/CoverageType', draft.claim.policy.coverageType);

    for (const item of draft.claim.damagedItems) {
      set(`DamagedItems/${item.itemId}/Description`, item.description);
      set(`DamagedItems/${item.itemId}/Category`, item.category);
      set(`DamagedItems/${item.itemId}/EstimatedAmount`, item.estimatedAmount);
    }
    for (const note of draft.claim.notes) {
      set(`Notes/${note.itemId}/Text`, note.text);
    }

    if (draft.claim.claimant && !this.nestedNodeType['Claimant']) {
      this.nestedNodeType['Claimant'] = draft.claim.claimant.type;
    }
    if (draft.claim.claimant) {
      const key = draft.claim.claimant.type === 'COMPANY' ? 'CompanyName' : 'FullName';
      if (!this.nestedNodeValues['Claimant']) this.nestedNodeValues['Claimant'] = {};
      if (!pending.has(`Claimant/${key}`)) {
        this.nestedNodeValues['Claimant'][key] = draft.claim.claimant.displayName;
      }
    }
  }

  private initNewItemForms(): void {
    if (!this.definition) return;
    for (const coll of this.definition.collections) {
      if (!this.newItemValues[coll.key]) this.newItemValues[coll.key] = {};
    }
  }
}
