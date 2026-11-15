import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { switchMap } from 'rxjs';
import { UserService } from '../../core/services/user.service';
import { SchemaFieldComponent } from './schema-field.component';
import { AggregateSummary, ModelApiService, ModelInfo, errorText } from './model-api.service';
import { KVFieldSchema, KVSchemaDocument, fieldAt, humanize, labelOf, optionsOf } from './schema';

// The aggregates of one model: the summary columns and the create form come from the model's info (its
// summary and create fields) and its schema (labels, controls, options).
@Component({
  selector: 'app-aggregate-list',
  standalone: true,
  imports: [CommonModule, RouterModule, SchemaFieldComponent],
  template: `
    <div class="space-y-6">
      <div class="rounded-2xl border border-gray-200 bg-white p-6 dark:border-gray-800 dark:bg-white/[0.03]">
        <div class="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <a routerLink="/models" class="text-sm font-medium text-brand-500 hover:text-brand-600">← Models</a>
            <h1 class="text-2xl font-semibold text-gray-900 dark:text-white">{{ info?.title ?? model }}</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">{{ info?.description }}</p>
          </div>
          <div class="flex flex-wrap items-center gap-2">
            @if (behind > 0) {
              <button class="rounded-lg border border-amber-300 bg-amber-50 px-4 py-2 text-sm font-medium text-amber-700 hover:bg-amber-100 dark:border-amber-900/60 dark:bg-amber-950/30 dark:text-amber-300" [disabled]="busy" (click)="migrateAll()">
                Migrate {{ behind }} to v{{ info?.schemaVersion }}
              </button>
            }
            @if (info?.hasLegacySample) {
              <button class="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800" [disabled]="busy" (click)="createLegacy()"
                title="Stores an aggregate in the old v0 layout; opening it (or Migrate) runs the migrations">+ Legacy v0 sample</button>
            }
            <button class="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800" (click)="load()">Refresh</button>
            <button class="rounded-lg bg-brand-500 px-4 py-2 text-sm font-medium text-white hover:bg-brand-600" (click)="showCreate = !showCreate">{{ showCreate ? 'Cancel' : 'New' }}</button>
          </div>
        </div>
        @if (message) { <p class="mt-3 text-sm text-emerald-600">{{ message }}</p> }

        @if (showCreate && schema) {
          <div class="mt-5 rounded-xl border border-gray-200 bg-gray-50 p-4 dark:border-gray-800 dark:bg-gray-900/40">
            <div class="grid gap-4 sm:grid-cols-2">
              @for (field of createFields; track field.path) {
                <app-schema-field [field]="field.schema" [path]="field.path" [value]="createValues[field.path] ?? null" (valueChange)="createValues[field.path] = $event"></app-schema-field>
              }
            </div>
            <button class="mt-4 h-11 rounded-lg bg-brand-500 px-6 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50" [disabled]="busy" (click)="create()">{{ busy ? 'Creating…' : 'Create & edit' }}</button>
          </div>
        }
      </div>

      @if (error) { <div class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">{{ error }}</div> }

      <div class="rounded-2xl border border-gray-200 bg-white p-6 dark:border-gray-800 dark:bg-white/[0.03]">
        <div class="overflow-x-auto">
          <table class="w-full min-w-[720px] text-left text-sm">
            <thead class="border-b border-gray-100 text-xs uppercase text-gray-500 dark:border-gray-800">
              <tr>
                @for (column of columns; track column.path) { <th class="py-3 pr-4">{{ column.label }}</th> }
                <th class="py-3 pr-4">Modified</th>
                <th class="py-3 pr-4">Schema</th>
                <th class="py-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-gray-100 dark:divide-gray-800">
              @for (row of rows; track row.id) {
                <tr class="text-gray-700 dark:text-gray-300">
                  @for (column of columns; track column.path) {
                    <td class="max-w-[260px] truncate py-3 pr-4" [ngClass]="$first ? 'font-medium text-gray-900 dark:text-white' : ''">
                      @if ($first && row.parentId) { <span class="mr-1 text-xs text-sky-500" title="Child aggregate">↳</span> }
                      {{ cell(column.field, row.summary[column.path]) }}
                    </td>
                  }
                  <td class="py-3 pr-4 text-gray-500">{{ row.modified | date: 'MMM d, HH:mm' }} · {{ row.modifiedBy }}</td>
                  <td class="py-3 pr-4"><span class="rounded px-1.5 py-0.5 text-xs" [ngClass]="row.schemaVersion < (info?.schemaVersion ?? 0) ? 'bg-amber-100 text-amber-700' : 'bg-gray-100 text-gray-500 dark:bg-gray-800'">v{{ row.schemaVersion }}</span></td>
                  <td class="py-3 text-right">
                    <a class="text-gray-500 hover:text-gray-700 dark:hover:text-gray-300" [routerLink]="['/m', model, row.id]">Open</a>
                    <button class="ml-3 font-medium text-brand-500 hover:text-brand-600" (click)="edit(row.id)">Edit</button>
                  </td>
                </tr>
              } @empty {
                <tr><td class="py-10 text-center text-gray-500" [attr.colspan]="columns.length + 3">{{ loading ? 'Loading…' : 'Nothing yet. Click New to create one.' }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `,
})
export class AggregateListComponent implements OnInit {
  model = '';
  info: ModelInfo | null = null;
  schema: KVSchemaDocument | null = null;
  rows: AggregateSummary[] = [];
  columns: Array<{ path: string; label: string; field: KVFieldSchema | null }> = [];
  createFields: Array<{ path: string; schema: KVFieldSchema }> = [];
  createValues: Record<string, unknown> = {};
  showCreate = false;
  loading = false;
  busy = false;
  error = '';
  message = '';

  constructor(
    private readonly api: ModelApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly users: UserService,
  ) {}

  get user(): string { return this.users.getUser() ?? 'unknown'; }

  get behind(): number { return this.rows.filter(r => r.schemaVersion < (this.info?.schemaVersion ?? 0)).length; }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      this.model = params.get('model') ?? '';
      this.api.info(this.model).subscribe({ next: info => { this.info = info; this.buildColumns(); }, error: e => (this.error = errorText(e)) });
      this.api.definition(this.model).subscribe({ next: schema => { this.schema = schema; this.buildColumns(); } });
      this.load();
    });
  }

  load(): void {
    this.loading = true;
    this.api.list(this.model).subscribe({
      next: rows => { this.rows = rows; this.loading = false; },
      error: e => { this.error = errorText(e); this.loading = false; },
    });
  }

  create(): void {
    this.busy = true;
    this.error = '';
    const values = Object.fromEntries(Object.entries(this.createValues).filter(([, v]) => v != null && v !== ''));
    this.api.create(this.model, this.user, values)
      .pipe(switchMap(snapshot => this.api.openDraft(this.model, snapshot.id, this.user)))
      .subscribe({
        next: draft => { this.busy = false; this.router.navigate(['/m', this.model, draft.aggregateId, 'drafts', draft.draftId]); },
        error: e => { this.busy = false; this.error = `Unable to create. ${errorText(e)}`; },
      });
  }

  edit(id: string): void {
    this.api.openDraft(this.model, id, this.user).subscribe({
      next: draft => this.router.navigate(['/m', this.model, id, 'drafts', draft.draftId]),
      error: e => (this.error = `Unable to open draft. ${errorText(e)}`),
    });
  }

  createLegacy(): void {
    this.busy = true;
    this.api.createLegacy(this.model, this.user).subscribe({
      next: () => { this.busy = false; this.message = 'Stored a v0 aggregate. Open it to migrate it on read, or migrate all at once.'; this.load(); },
      error: e => { this.busy = false; this.error = errorText(e); },
    });
  }

  migrateAll(): void {
    this.busy = true;
    this.api.migrateAll(this.model).subscribe({
      next: result => { this.busy = false; this.message = `Migrated ${result.migrated} of ${result.scanned} (${result.commits} migration commits) to v${result.schemaVersion}.`; this.load(); },
      error: e => { this.busy = false; this.error = errorText(e); },
    });
  }

  cell(field: KVFieldSchema | null, value: unknown): string {
    if (value == null || value === '') return '—';
    const options = field ? optionsOf(field) : [];
    const label = (v: unknown) => options.find(o => o.id === String(v))?.label ?? String(v);
    return Array.isArray(value) ? value.map(label).join(', ') : label(value);
  }

  private buildColumns(): void {
    if (!this.info || !this.schema) return;
    const schema = this.schema;
    this.columns = this.info.summaryFields.map(path => {
      const field = fieldAt(schema, path);
      return { path, field, label: field ? labelOf(field) : humanize(path.split('/').pop() ?? path) };
    });
    this.createFields = this.info.createFields
      .map(path => ({ path, schema: fieldAt(schema, path) }))
      .filter((f): f is { path: string; schema: KVFieldSchema } => f.schema != null);
  }
}
