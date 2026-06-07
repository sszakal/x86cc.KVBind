import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ChangeTreeTableComponent } from './change-tree-table.component';
import { ClaimApiService, ClaimChangeResponse } from './claim-api.service';
import { UserService } from '../../core/services/user.service';

@Component({
  selector: 'app-claim-review',
  standalone: true,
  imports: [CommonModule, RouterModule, ChangeTreeTableComponent],
  template: `
    <div class="mx-auto max-w-4xl space-y-6">
      <div class="rounded-2xl border border-gray-200 bg-white p-6 dark:border-gray-800 dark:bg-white/[0.03]">
        <a class="text-sm font-medium text-brand-500 hover:text-brand-600"
          [routerLink]="['/claims', claimId, 'drafts', draftId]">← Back to editing</a>
        <h1 class="mt-0.5 text-2xl font-semibold text-gray-900 dark:text-white">Review changes</h1>
        <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
          These are the changes your draft will commit to the claim snapshot. Confirm to commit.
        </p>
      </div>

      @if (error) {
        <div class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300">{{ error }}</div>
      }

      @if (loading) {
        <p class="text-sm text-gray-500">Loading…</p>
      } @else {
        <div class="rounded-2xl border border-gray-200 bg-white p-6 dark:border-gray-800 dark:bg-white/[0.03]">
          <div class="mb-3 flex items-center justify-between">
            <h2 class="text-base font-semibold text-gray-900 dark:text-white">Change set</h2>
            <span class="rounded-full px-2.5 py-0.5 text-xs font-bold"
              [ngClass]="changes.length > 0 ? 'bg-orange-100 text-orange-700 dark:bg-orange-950/40 dark:text-orange-300' : 'bg-gray-100 text-gray-500 dark:bg-gray-800'">
              {{ changes.length }} change{{ changes.length !== 1 ? 's' : '' }}
            </span>
          </div>
          @if (changes.length > 0) {
            <p class="mb-3 text-xs text-gray-400">
              <span class="text-green-600 dark:text-green-400">Green = new value</span> ·
              <span class="text-red-500 line-through dark:text-red-400">Red = old value</span>
            </p>
            <app-change-tree-table [changes]="changes"></app-change-tree-table>
          } @else {
            <p class="py-8 text-center text-sm text-gray-400">No changes to commit.</p>
          }
        </div>

        <div class="sticky bottom-0 flex items-center justify-between gap-3 rounded-2xl border border-gray-200 bg-white/90 p-4 backdrop-blur dark:border-gray-800 dark:bg-gray-900/80">
          <button class="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800"
            [routerLink]="['/claims', claimId, 'drafts', draftId]">
            Back to editing
          </button>
          <button class="rounded-lg bg-gray-900 px-6 py-2 text-sm font-semibold text-white hover:bg-gray-800 disabled:opacity-50 dark:bg-white dark:text-gray-900"
            [disabled]="committing || changes.length === 0" (click)="confirm()">
            {{ committing ? 'Committing…' : 'Confirm & commit' }}
          </button>
        </div>
      }
    </div>
  `,
})
export class ClaimReviewComponent implements OnInit {
  claimId = '';
  draftId = '';
  changes: ClaimChangeResponse[] = [];
  loading = false;
  committing = false;
  error = '';

  constructor(
    private readonly api: ClaimApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly userService: UserService,
  ) {}

  ngOnInit(): void {
    this.claimId = this.route.snapshot.paramMap.get('claimId') ?? '';
    this.draftId = this.route.snapshot.paramMap.get('draftId') ?? '';
    this.load();
  }

  private get username(): string { return this.userService.getUser() ?? 'unknown'; }

  private load(): void {
    this.loading = true;
    this.api.getDraft(this.claimId, this.draftId).subscribe({
      next: draft => {
        this.loading = false;
        if (draft.isRebasing) {
          this.router.navigate(['/claims', this.claimId, 'drafts', this.draftId, 'rebase']);
          return;
        }
        this.changes = draft.changes;
      },
      error: err => {
        this.loading = false;
        this.error = `Unable to load changes. ${err.message ?? ''}`;
      },
    });
  }

  confirm(): void {
    this.committing = true;
    this.error = '';
    this.api.commitDraft(this.claimId, this.draftId, { user: this.username }).subscribe({
      next: () => this.router.navigate(['/claims', this.claimId]),
      error: err => {
        if (err instanceof HttpErrorResponse && err.status === 409) {
          // Stale — rebase. Auto-merge retries the commit; conflicts go to the merge screen.
          this.rebaseThenCommit();
        } else {
          this.error = `Unable to commit. ${err.message ?? ''}`;
          this.committing = false;
        }
      },
    });
  }

  private rebaseThenCommit(): void {
    this.api.beginRebase(this.claimId, this.draftId).subscribe({
      next: result => {
        // Anything pulled in (incoming changes and/or conflicts) gets reviewed before committing.
        if (result.outcome !== 'AlreadyCurrent') {
          this.committing = false;
          this.router.navigate(['/claims', this.claimId, 'drafts', this.draftId, 'rebase']);
        } else {
          this.confirm();
        }
      },
      error: err => {
        this.error = `Unable to rebase. ${err.message ?? ''}`;
        this.committing = false;
      },
    });
  }
}
