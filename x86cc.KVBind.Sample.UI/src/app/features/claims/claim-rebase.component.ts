import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ClaimApiService, RebaseConflictResponse } from './claim-api.service';

interface ConflictRow extends RebaseConflictResponse {
  customValue: string;
  busy: boolean;
}

@Component({
  selector: 'app-claim-rebase',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="mx-auto max-w-4xl space-y-6">
      <div class="rounded-2xl border border-amber-200 bg-amber-50 p-6 dark:border-amber-900/60 dark:bg-amber-950/20">
        <div class="flex items-start justify-between gap-4">
          <div>
            <p class="text-sm font-medium text-amber-600 dark:text-amber-400">Merge required</p>
            <h1 class="text-2xl font-semibold text-gray-900 dark:text-white">Resolve conflicts</h1>
            <p class="mt-1 text-sm text-gray-600 dark:text-gray-400">
              The claim changed upstream while you were editing. Pick a value for each conflict below,
              then finish the merge. Non-conflicting upstream changes are merged automatically.
            </p>
          </div>
          <span class="shrink-0 rounded-full bg-amber-100 px-3 py-1 text-xs font-medium text-amber-700 dark:bg-amber-900/40 dark:text-amber-300">
            {{ resolvedCount }}/{{ rows.length }} resolved
          </span>
        </div>
      </div>

      @if (error) {
        <div class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">{{ error }}</div>
      }

      @if (loading) {
        <p class="text-sm text-gray-500">Loading…</p>
      }

      @for (row of rows; track row.path) {
        <div class="rounded-2xl border bg-white p-5 dark:bg-white/[0.03]"
          [ngClass]="row.resolution === 'Unresolved'
            ? 'border-gray-200 dark:border-gray-800'
            : 'border-emerald-300 dark:border-emerald-800'">
          <div class="flex items-center justify-between">
            <div>
              <code class="text-sm font-medium text-gray-900 dark:text-white">{{ displayPath(row.path) }}</code>
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
              {{ isItems(row.path)
                ? 'You and the upstream changed which items are in this collection. Membership cannot be merged item-by-item — pick one side.'
                : 'You and the upstream chose different types for this node. The fields differ per type, so the whole node must come from one side.' }}
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
                [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">
                Take theirs (whole node)
              </button>
              <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                [ngClass]="btnClass(row, 'Ours')" [disabled]="row.busy" (click)="resolve(row, 'Ours')">
                Keep mine (whole node)
              </button>
            </div>
          } @else if (row.kind === 'DeleteEdit') {
            <p class="mt-3 text-sm text-gray-600 dark:text-gray-400">
              You deleted this, but it was modified upstream.
            </p>
            <div class="mt-3 flex flex-wrap gap-2">
              <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                [ngClass]="btnClass(row, 'Ours')" [disabled]="row.busy" (click)="resolve(row, 'Ours')">
                Keep my deletion
              </button>
              <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">
                Restore upstream
              </button>
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
                [ngClass]="btnClass(row, 'Theirs')" [disabled]="row.busy" (click)="resolve(row, 'Theirs')">
                Take theirs
              </button>
              <button class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                [ngClass]="btnClass(row, 'Ours')" [disabled]="row.busy" (click)="resolve(row, 'Ours')">
                Keep mine
              </button>
              <span class="text-xs text-gray-400">or</span>
              <input class="h-9 w-40 rounded-lg border border-gray-300 px-3 text-sm dark:border-gray-700 dark:bg-gray-900 dark:text-white"
                [(ngModel)]="row.customValue" placeholder="custom value" [disabled]="row.busy" />
              <button class="rounded-lg border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
                [disabled]="row.busy || !row.customValue" (click)="resolve(row, 'Custom')">
                Use custom
              </button>
            </div>
          }
        </div>
      }

      <div class="sticky bottom-0 flex items-center justify-between gap-3 rounded-2xl border border-gray-200 bg-white/90 p-4 backdrop-blur dark:border-gray-800 dark:bg-gray-900/80">
        <button class="rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 dark:border-red-900/60 dark:hover:bg-red-950/30"
          [disabled]="busy" (click)="discardAll()">
          Discard all my changes
        </button>
        <div class="flex items-center gap-2">
          <button class="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
            [disabled]="busy" (click)="cancel()">
            Cancel merge
          </button>
          <button class="rounded-lg bg-brand-500 px-6 py-2 text-sm font-medium text-white hover:bg-brand-600 disabled:opacity-50"
            [disabled]="busy || resolvedCount < rows.length" (click)="finish()">
            {{ busy ? 'Finishing…' : 'Finish merge' }}
          </button>
        </div>
      </div>
    </div>
  `,
})
export class ClaimRebaseComponent implements OnInit {
  claimId = '';
  draftId = '';
  rows: ConflictRow[] = [];
  loading = false;
  busy = false;
  error = '';

  constructor(
    private readonly api: ClaimApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
  ) {}

  ngOnInit(): void {
    this.claimId = this.route.snapshot.paramMap.get('claimId') ?? '';
    this.draftId = this.route.snapshot.paramMap.get('draftId') ?? '';
    this.load();
  }

  get resolvedCount(): number {
    return this.rows.filter(r => r.resolution !== 'Unresolved').length;
  }

  private load(): void {
    this.loading = true;
    this.api.getDraft(this.claimId, this.draftId).subscribe({
      next: draft => {
        this.loading = false;
        if (!draft.isRebasing) {
          // Nothing (or no longer anything) to merge — back to the editor.
          this.toEditor();
          return;
        }
        this.rows = draft.conflicts.map(c => ({ ...c, customValue: '', busy: false }));
      },
      error: err => {
        this.loading = false;
        this.error = `Unable to load merge. ${err.message ?? ''}`;
      },
    });
  }

  resolve(row: ConflictRow, resolution: 'Ours' | 'Theirs' | 'Custom'): void {
    row.busy = true;
    this.error = '';
    this.api
      .resolveRebaseConflict(this.claimId, this.draftId, {
        path: row.path,
        resolution,
        value: resolution === 'Custom' ? row.customValue : undefined,
      })
      .subscribe({
        next: result => {
          row.busy = false;
          const updated = result.conflicts.find(c => c.path === row.path);
          if (updated) row.resolution = updated.resolution;
        },
        error: err => {
          row.busy = false;
          this.error = `Unable to resolve. ${err.error?.error ?? err.message ?? ''}`;
        },
      });
  }

  finish(): void {
    this.busy = true;
    this.error = '';
    this.api.finishRebase(this.claimId, this.draftId).subscribe({
      next: () => this.toEditor(),
      error: err => {
        this.busy = false;
        this.error = `Unable to finish merge. ${err.error?.error ?? err.message ?? ''}`;
      },
    });
  }

  discardAll(): void {
    this.busy = true;
    this.api.resetDraft(this.claimId, this.draftId).subscribe({
      next: () => this.toEditor(),
      error: err => {
        this.busy = false;
        this.error = `Unable to discard. ${err.message ?? ''}`;
      },
    });
  }

  cancel(): void {
    this.busy = true;
    this.api.cancelRebase(this.claimId, this.draftId).subscribe({
      next: () => this.toEditor(),
      error: err => {
        this.busy = false;
        this.error = `Unable to cancel. ${err.message ?? ''}`;
      },
    });
  }

  private toEditor(): void {
    this.router.navigate(['/claims', this.claimId, 'drafts', this.draftId]);
  }

  btnClass(row: ConflictRow, choice: string): string {
    return row.resolution === choice
      ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300'
      : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800';
  }

  resolutionLabel(row: ConflictRow): string {
    if (row.kind === 'DeleteEdit') return row.resolution === 'Ours' ? 'Kept deletion' : 'Restored upstream';
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

  isItems(path: string): boolean {
    return path === '$items' || path.endsWith('/$items');
  }

  // Drops the trailing $type / $items technical segment so the user sees the node, not the internals.
  displayPath(path: string): string {
    return path
      .split('/')
      .filter(s => s !== '$type' && s !== '$items')
      .join('/') || path;
  }

  display(value: unknown): string {
    if (value === null || value === undefined) return '(none)';
    // A membership array is a list of item GUIDs — show the count, never the raw GUIDs.
    if (Array.isArray(value)) {
      const n = value.length;
      return `${n} item${n !== 1 ? 's' : ''}`;
    }
    return String(value);
  }
}
