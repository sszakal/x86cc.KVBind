import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { switchMap } from 'rxjs';
import { ChangeTreeTableComponent } from '../../shared/components/change-tree-table.component';
import { UserService } from '../../core/services/user.service';
import { DataViewComponent } from './data-view.component';
import { AggregateSnapshot, ChangeSet, ModelApiService, ModelInfo, errorText } from './model-api.service';
import { KVSchemaDocument } from './schema';

// One aggregate's committed state (rendered from the schema) and its history: every commit with its
// changes and the domain events it published, including migration commits.
@Component({
  selector: 'app-aggregate-detail',
  standalone: true,
  imports: [CommonModule, RouterModule, ChangeTreeTableComponent, DataViewComponent],
  template: `
    <div class="mb-6 flex flex-wrap items-center justify-between gap-3">
      <div>
        <a [routerLink]="['/m', model]" class="text-sm font-medium text-brand-500 hover:text-brand-600">← {{ info?.title ?? model }}</a>
        <h1 class="mt-0.5 text-xl font-bold text-gray-900 dark:text-white"><span class="font-mono text-lg">{{ id }}</span></h1>
        @if (snapshot) {
          <p class="text-xs text-gray-500">schema v{{ snapshot.schemaVersion }} · last commit {{ snapshot.lastCommitTimestamp | date: 'medium' }}
            @if (snapshot.parentId) { · child of <a class="font-mono text-brand-500" [routerLink]="['/m', model, snapshot.parentId]">{{ snapshot.parentId.slice(0, 8) }}</a> }
          </p>
        }
      </div>
      <div class="flex gap-2">
        @if (info?.supportsChildren && snapshot && !snapshot.parentId) {
          <button class="h-9 rounded-lg border border-sky-300 px-4 text-sm font-semibold text-sky-700 hover:bg-sky-50 dark:border-sky-900 dark:text-sky-300" [disabled]="busy" (click)="createChild()">New child</button>
        }
        <button class="h-9 rounded-lg bg-brand-500 px-4 text-sm font-semibold text-white hover:bg-brand-600" (click)="edit()">Edit</button>
      </div>
    </div>

    @if (error) { <div class="mb-4 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">{{ error }}</div> }

    @if (snapshot && schema) {
      <div class="mb-5 flex gap-1 border-b border-gray-200 dark:border-gray-800">
        @for (tab of tabs; track tab) {
          <button class="rounded-t-lg px-4 py-2 text-sm font-medium" [ngClass]="active === tab ? 'border-b-2 border-brand-500 text-brand-600 dark:text-brand-400' : 'text-gray-500 hover:text-gray-700'" (click)="active = tab">{{ tab }}</button>
        }
      </div>

      @if (active === 'Data') {
        <section class="rounded-2xl border border-gray-200 bg-white p-6 dark:border-gray-800 dark:bg-white/[0.03]">
          @if (computedEntries().length > 0) {
            <div class="mb-5 flex flex-wrap gap-2">
              @for (entry of computedEntries(); track entry[0]) {
                <span class="rounded-full bg-emerald-50 px-3 py-1 text-xs font-semibold text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300">{{ entry[0] }}: {{ entry[1] }}</span>
              }
            </div>
          }
          <app-data-view [node]="schema.root" [data]="snapshot.data" [inheritedPaths]="snapshot.inheritedPaths"></app-data-view>
        </section>
      }

      @if (active === 'History') {
        <div class="grid gap-6 xl:grid-cols-[420px_1fr]">
          <div class="space-y-3">
            @for (changeset of changesets; track changeset.commitId) {
              <button class="block w-full rounded-xl border p-4 text-left transition-colors" [ngClass]="selected?.commitId === changeset.commitId ? 'border-brand-500 bg-brand-50 dark:bg-brand-950/20' : 'border-gray-200 hover:bg-gray-50 dark:border-gray-800 dark:hover:bg-gray-900'" (click)="selected = changeset">
                <p class="font-mono text-xs text-gray-500">{{ changeset.commitId }}</p>
                <p class="mt-1 text-sm text-gray-700 dark:text-gray-300">{{ changeset.user }} · {{ changeset.timestamp | date: 'medium' }}</p>
                <p class="mt-1 text-xs text-gray-500">
                  {{ changeset.changes.length }} change(s)
                  @if (changeset.events.length) { · <span class="text-indigo-600">{{ changeset.events.length }} event(s)</span> }
                  @if (changeset.migrationToVersion != null) { · <span class="text-amber-600">migration → v{{ changeset.migrationToVersion }}</span> }
                </p>
              </button>
            } @empty { <p class="text-sm text-gray-500">No commits.</p> }
            @if (changesets.length < totalChangesets) {
              <button class="w-full rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800" [disabled]="busy" (click)="loadMoreHistory()">
                Load more ({{ changesets.length }} of {{ totalChangesets }})
              </button>
            }
          </div>
          <div>
            @if (selected) {
              <app-change-tree-table [changes]="selected.changes"></app-change-tree-table>
              @if (selected.events.length) {
                <h3 class="mb-2 mt-5 text-sm font-semibold text-gray-900 dark:text-white">Domain events published</h3>
                @for (ev of selected.events; track $index) {
                  <div class="mb-2 rounded-lg border border-gray-100 px-3 py-2 text-sm dark:border-gray-800">
                    <span class="font-semibold text-indigo-600 dark:text-indigo-300">{{ ev.type }}</span>
                    <span class="ml-2 font-mono text-xs text-gray-500">{{ ev.payload }}</span>
                  </div>
                }
              }
            }
          </div>
        </div>
      }
    } @else if (!error) {
      <p class="text-gray-500">Loading…</p>
    }
  `,
})
export class AggregateDetailComponent implements OnInit {
  model = '';
  id = '';
  info: ModelInfo | null = null;
  schema: KVSchemaDocument | null = null;
  snapshot: AggregateSnapshot | null = null;
  changesets: ChangeSet[] = [];
  totalChangesets = 0;
  selected: ChangeSet | null = null;
  readonly tabs = ['Data', 'History'];
  active = 'Data';
  busy = false;
  error = '';

  constructor(
    private readonly api: ModelApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly users: UserService,
  ) {}

  get user(): string { return this.users.getUser() ?? 'unknown'; }

  ngOnInit(): void {
    this.route.paramMap.subscribe(params => {
      this.model = params.get('model') ?? '';
      this.id = params.get('id') ?? '';
      this.api.info(this.model).subscribe({ next: i => (this.info = i) });
      this.api.definition(this.model).subscribe({ next: s => (this.schema = s) });
      this.api.snapshot(this.model, this.id).subscribe({ next: s => (this.snapshot = s), error: e => (this.error = errorText(e)) });
      this.api.changeSets(this.model, this.id).subscribe({
        next: page => { this.changesets = page.items; this.totalChangesets = page.total; this.selected = page.items[0] ?? null; },
      });
    });
  }

  // The next page of older commits.
  loadMoreHistory(): void {
    this.busy = true;
    this.api.changeSets(this.model, this.id, this.changesets.length).subscribe({
      next: page => { this.changesets = [...this.changesets, ...page.items]; this.totalChangesets = page.total; this.busy = false; },
      error: e => { this.error = errorText(e); this.busy = false; },
    });
  }

  computedEntries(): Array<[string, unknown]> { return Object.entries(this.snapshot?.computed ?? {}); }

  edit(): void {
    this.api.openDraft(this.model, this.id, this.user).subscribe({
      next: draft => this.router.navigate(['/m', this.model, this.id, 'drafts', draft.draftId]),
      error: e => (this.error = errorText(e)),
    });
  }

  createChild(): void {
    this.busy = true;
    this.api.create(this.model, this.user, {}, this.id)
      .pipe(switchMap(child => this.api.openDraft(this.model, child.id, this.user)))
      .subscribe({
        next: draft => { this.busy = false; this.router.navigate(['/m', this.model, draft.aggregateId, 'drafts', draft.draftId]); },
        error: e => { this.busy = false; this.error = errorText(e); },
      });
  }
}
