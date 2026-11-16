import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ModelApiService, RebaseConflict, errorText } from './model-api.service';

interface ConflictRow extends RebaseConflict {
  customValue: string;
  busy: boolean;
}

@Component({
  selector: 'app-draft-rebase',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="mx-auto max-w-4xl space-y-6">
      <div class="rounded-2xl border border-amber-200 bg-amber-50 p-6 dark:border-amber-900/60 dark:bg-amber-950/20">
        <div class="flex items-start justify-between gap-4">
          <div>
            <p class="text-sm font-medium text-amber-600 dark:text-amber-400">Review upstream changes</p>
            <h1 class="text-2xl font-semibold text-gray-900 dark:text-white">Review &amp; merge</h1>
            <p class="mt-1 text-sm text-gray-600 dark:text-gray-400">
              This aggregate changed upstream while you were editing. Review what was pulled in — accept or
              reject each incoming change — and resolve any conflicts, then finish the merge.
            </p>
          </div>
          @if (conflictRows.length > 0) {
            <span class="shrink-0 rounded-full bg-amber-100 px-3 py-1 text-xs font-medium text-amber-700 dark:bg-amber-900/40 dark:text-amber-300">
              {{ resolvedConflictCount }}/{{ conflictRows.length }} conflicts resolved
            </span>
          }
        </div>
      </div>

      @if (error) {
        <div class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">{{ error }}</div>
      }
      @if (loading) {
        <p class="text-sm text-gray-500">Loading…</p>
      }

      <!-- ═══ CONFLICTS — require a decision ═══ -->
      @if (conflictRows.length > 0) {
        <div>
          <h2 class="mb-2 text-sm font-semibold uppercase tracking-wide text-gray-500">Conflicts · need your decision</h2>
          @for (row of conflictRows; track row.path) {
            <div class="mb-3 rounded-2xl border bg-white p-5 dark:bg-white/[0.03]"
              [ngClass]="row.resolution === 'Unresolved'
                ? 'border-amber-300 dark:border-amber-800'
                : 'border-emerald-300 dark:border-emerald-800'">
              <div class="flex items-center justify-between">
                <div>
                  <code class="text-sm font-medium text-gray-900 dark:text-white">{{ row.displayPath || row.path }}</code>
                  <span class="ml-2 rounded bg-gray-100 px-1.5 py-0.5 text-[11px] font-medium uppercase text-gray-500 dark:bg-gray-800 dark:text-gray-400">
                    {{ kindLabel(row.kind) }}
                  </span>
                </div>
                @if (row.resolution !== 'Unresolved') {
                  <span class="text-xs font-medium text-emerald-600 dark:text-emerald-400">✓ {{ resolutionLabel(row) }}</span>
                }
              </div>

              @if (row.kind === 'Structural') {
                <p class="mt-3 text-sm text-gray-600 dark:text-gray-400">
                  You and the upstream chose different types for this node. The fields differ per type, so the whole node must come from one side.
                </p>
                <div class="mt-4 grid gap-3 sm:grid-cols-2">
                  <div class="rounded-lg border border-blue-200 bg-blue-50/50 p-3 dark:border-blue-900/50 dark:bg-blue-950/20">
                    <p class="text-[11px] font-medium uppercase text-blue-500">Upstream (theirs)</p>
                    <p class="mt-1 text-sm text-gray-700 dark:text-gray-200">{{ display(row.mainValue) }}</p>
                  </div>
                  <div class="rounded-lg border border-brand-200 bg-brand-50/50 p-3 dark:border-brand-900/50 dark:bg-brand-950/20">
                    <p class="text-[11px] font-medium uppercase text-brand-500">Mine (ours)</p>
                    <p class="mt-1 text-sm text-gray-700 dark:text-gray-200">{{ display(row.oursValue) }}</p>
                  </div>
                </div>
                <div class="mt-3 flex flex-wrap gap-2">
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">Take theirs (whole node)</button>
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Ours')" [disabled]="row.busy" (click)="resolve(row, 'Ours')">Keep mine (whole node)</button>
                </div>
              } @else if (row.kind === 'DeleteEdit' && row.oursValue == null) {
                <p class="mt-3 text-sm text-gray-600 dark:text-gray-400">You deleted this, but it was modified upstream.</p>
                <div class="mt-3 flex flex-wrap gap-2">
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Ours')" [disabled]="row.busy" (click)="resolve(row, 'Ours')">Keep my deletion</button>
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">Restore upstream version</button>
                </div>
              } @else if (row.kind === 'DeleteEdit' && row.oursValue != null) {
                <p class="mt-3 text-sm text-gray-600 dark:text-gray-400">This was deleted upstream, but you have edits on it.</p>
                <div class="mt-3 flex flex-wrap gap-2">
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Ours')" [disabled]="row.busy" (click)="resolve(row, 'Ours')">Keep my edits (restore item)</button>
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">Accept deletion</button>
                </div>
              } @else {
                <div class="mt-4 grid gap-3 sm:grid-cols-3">
                  <div class="rounded-lg border border-gray-200 p-3 dark:border-gray-800">
                    <p class="text-[11px] font-medium uppercase text-gray-400">Original</p>
                    <p class="mt-1 text-sm text-gray-600 dark:text-gray-300">{{ display(row.baseValue) }}</p>
                  </div>
                  <div class="rounded-lg border border-blue-200 bg-blue-50/50 p-3 dark:border-blue-900/50 dark:bg-blue-950/20">
                    <p class="text-[11px] font-medium uppercase text-blue-500">Upstream (theirs)</p>
                    <p class="mt-1 text-sm text-gray-700 dark:text-gray-200">{{ display(row.mainValue) }}</p>
                  </div>
                  <div class="rounded-lg border border-brand-200 bg-brand-50/50 p-3 dark:border-brand-900/50 dark:bg-brand-950/20">
                    <p class="text-[11px] font-medium uppercase text-brand-500">Mine (ours)</p>
                    <p class="mt-1 text-sm text-gray-700 dark:text-gray-200">{{ display(row.oursValue) }}</p>
                  </div>
                </div>
                <div class="mt-3 flex flex-wrap items-center gap-2">
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">Take theirs</button>
                  <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                    [ngClass]="btnClass(row, 'Ours')" [disabled]="row.busy" (click)="resolve(row, 'Ours')">Keep mine</button>
                  <span class="text-xs text-gray-400">or</span>
                  <input class="h-9 w-40 rounded-lg border border-gray-300 px-3 text-sm dark:border-gray-700 dark:bg-gray-900 dark:text-white"
                    [(ngModel)]="row.customValue" placeholder="custom value" [disabled]="row.busy" />
                  <button class="rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
                    [disabled]="row.busy || !row.customValue" (click)="resolve(row, 'Custom')">Use custom</button>
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- ═══ FIXED — reported, no choice ═══ -->
      @if (fixedRows.length > 0) {
        <div>
          <h2 class="mb-2 text-sm font-semibold uppercase tracking-wide text-gray-500">Order · taken from upstream</h2>
          @for (row of fixedRows; track row.path) {
            <div class="mb-2 rounded-xl border border-gray-200 bg-white p-4 dark:border-gray-800 dark:bg-white/[0.03]">
              <code class="text-sm font-medium text-gray-900 dark:text-white">{{ row.displayPath || row.path }}</code>
              <p class="mt-0.5 text-xs text-gray-500 dark:text-gray-400">
                You reordered this list, and its items also changed upstream. The upstream order is kept, with your new items at the end.
              </p>
              <div class="mt-3 grid gap-3 sm:grid-cols-2">
                <div class="rounded-lg border border-blue-200 bg-blue-50/50 p-3 dark:border-blue-900/50 dark:bg-blue-950/20">
                  <p class="text-[11px] font-medium uppercase text-blue-500">Upstream order (kept)</p>
                  <p class="mt-1 text-sm text-gray-700 dark:text-gray-200">{{ order(row.mainValue) }}</p>
                </div>
                <div class="rounded-lg border border-brand-200 bg-brand-50/50 p-3 dark:border-brand-900/50 dark:bg-brand-950/20">
                  <p class="text-[11px] font-medium uppercase text-brand-500">Your order</p>
                  <p class="mt-1 text-sm text-gray-700 dark:text-gray-200">{{ order(row.oursValue) }}</p>
                </div>
              </div>
            </div>
          }
        </div>
      }

      <!-- ═══ INCOMING — default accepted, rejectable ═══ -->
      @if (incomingRows.length > 0) {
        <div>
          <h2 class="mb-2 text-sm font-semibold uppercase tracking-wide text-gray-500">Incoming changes · accepted by default</h2>
          @for (row of incomingRows; track row.path) {
            <div class="mb-2 flex flex-wrap items-center justify-between gap-3 rounded-xl border bg-white p-4 dark:bg-white/[0.03]"
              [ngClass]="row.resolution === 'Ours'
                ? 'border-red-200 dark:border-red-900/60'
                : 'border-gray-200 dark:border-gray-800'">
              <div class="min-w-0">
                <code class="text-sm font-medium text-gray-900 dark:text-white">{{ row.displayPath || row.path }}</code>
                <p class="mt-0.5 text-xs text-gray-500 dark:text-gray-400">{{ incomingSummary(row) }}</p>
              </div>
              <div class="flex shrink-0 items-center gap-2">
                <button class="rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors"
                  [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">Accept</button>
                <button class="rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors"
                  [ngClass]="row.resolution === 'Ours'
                    ? 'border-red-400 bg-red-50 text-red-700 dark:bg-red-950/30 dark:text-red-300'
                    : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800'"
                  [disabled]="row.busy" (click)="resolve(row, 'Ours')">Reject</button>
              </div>
            </div>
          }
        </div>
      }

      <div class="sticky bottom-0 flex items-center justify-between gap-3 rounded-2xl border border-gray-200 bg-white/90 p-4 backdrop-blur dark:border-gray-800 dark:bg-gray-900/80">
        <button class="rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 dark:border-red-900/60 dark:hover:bg-red-950/30"
          [disabled]="busy" (click)="discardAll()">Discard all my changes</button>
        <div class="flex items-center gap-2">
          <button class="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
            [disabled]="busy" (click)="cancel()">Cancel merge</button>
          <button class="rounded-lg bg-brand-500 px-6 py-2 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50"
            [disabled]="busy || unresolvedConflictCount > 0" (click)="finish()">
            {{ busy ? 'Finishing…' : 'Finish merge' }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class DraftRebaseComponent implements OnInit {
  model = '';
  id = '';
  draftId = '';
  rows: ConflictRow[] = [];
  loading = false;
  busy = false;
  error = '';

  constructor(
    private readonly api: ModelApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.model = this.route.snapshot.paramMap.get('model') ?? '';
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    this.draftId = this.route.snapshot.paramMap.get('draftId') ?? '';
    this.load();
  }

  get conflictRows(): ConflictRow[] { return this.rows.filter(r => r.requiresResolution); }
  get incomingRows(): ConflictRow[] { return this.rows.filter(r => r.isIncoming); }
  get fixedRows(): ConflictRow[] { return this.rows.filter(r => r.isFixed); }

  get resolvedConflictCount(): number {
    return this.conflictRows.filter(r => r.resolution !== 'Unresolved').length;
  }

  get unresolvedConflictCount(): number {
    return this.conflictRows.filter(r => r.resolution === 'Unresolved').length;
  }

  private load(): void {
    this.loading = true;
    this.api.draft(this.model, this.id, this.draftId).subscribe({
      next: draft => {
        this.loading = false;
        if (!draft.isRebasing) {
          this.toEditor();
          return;
        }
        this.rows = draft.conflicts.map(c => ({ ...c, customValue: '', busy: false }));
      },
      error: err => {
        this.loading = false;
        this.error = `Unable to load merge. ${errorText(err)}`;
      },
    });
  }

  resolve(row: ConflictRow, resolution: 'Ours' | 'Theirs' | 'Custom'): void {
    row.busy = true;
    this.error = '';
    this.api
      .resolveConflict(this.model, this.id, this.draftId, row.path, resolution, resolution === 'Custom' ? row.customValue : undefined)
      .subscribe({
        next: result => {
          row.busy = false;
          const updated = result.conflicts.find(c => c.path === row.path);
          if (updated) row.resolution = updated.resolution;
        },
        error: err => {
          row.busy = false;
          this.error = `Unable to resolve. ${errorText(err)}`;
        },
      });
  }

  finish(): void {
    this.busy = true;
    this.error = '';
    this.api.finishRebase(this.model, this.id, this.draftId).subscribe({
      next: () => this.toEditor(),
      error: err => {
        this.busy = false;
        this.error = `Unable to finish merge. ${errorText(err)}`;
      },
    });
  }

  discardAll(): void {
    this.busy = true;
    this.api.reset(this.model, this.id, this.draftId).subscribe({
      next: () => this.toEditor(),
      error: err => {
        this.busy = false;
        this.error = `Unable to discard. ${errorText(err)}`;
      },
    });
  }

  cancel(): void {
    this.busy = true;
    this.api.cancelRebase(this.model, this.id, this.draftId).subscribe({
      next: () => this.toEditor(),
      error: err => {
        this.busy = false;
        this.error = `Unable to cancel. ${errorText(err)}`;
      },
    });
  }

  private toEditor(): void {
    this.router.navigate(['/m', this.model, this.id, 'drafts', this.draftId]);
  }

  btnClass(row: ConflictRow, choice: string): string {
    return row.resolution === choice
      ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300'
      : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800';
  }

  resolutionLabel(row: ConflictRow): string {
    if (row.kind === 'DeleteEdit') {
      if (row.oursValue == null) return row.resolution === 'Ours' ? 'Kept deletion' : 'Restored upstream';
      return row.resolution === 'Ours' ? 'Kept my edits' : 'Accepted deletion';
    }
    return (
      ({ Ours: 'Kept mine', Theirs: 'Took theirs', Custom: 'Custom value' } as Record<string, string>)[row.resolution] ??
      row.resolution
    );
  }

  kindLabel(kind: string): string {
    if (kind === 'DeleteEdit') return 'delete / edit';
    if (kind === 'Structural') return 'whole node';
    return 'value';
  }

  // One-line description of an incoming change for the review list.
  incomingSummary(row: ConflictRow): string {
    if (row.kind === 'IncomingItem') {
      return row.baseValue == null ? 'New item added upstream' : 'Item removed upstream';
    }
    if (row.oursValue != null && this.display(row.oursValue) === this.display(row.mainValue)) {
      return `Both set to "${this.display(row.mainValue)}" — already in sync`;
    }
    if (row.baseValue == null) return `Added upstream: ${this.display(row.mainValue)}`;
    if (row.mainValue == null) return `Removed upstream (was ${this.display(row.baseValue)})`;
    return `${this.display(row.baseValue)} → ${this.display(row.mainValue)}`;
  }


  // The item ids of a collection's membership list, in order.
  order(value: unknown): string {
    return Array.isArray(value) && value.length > 0 ? value.join(' → ') : '(none)';
  }

  display(value: unknown): string {
    if (value === null || value === undefined) return '(none)';
    if (Array.isArray(value)) {
      const n = value.length;
      return `${n} item${n !== 1 ? 's' : ''}`;
    }
    return String(value);
  }
}
