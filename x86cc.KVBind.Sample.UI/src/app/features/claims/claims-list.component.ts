import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { switchMap } from 'rxjs';
import { Router, RouterModule } from '@angular/router';
import { ClaimApiService, ClaimSummaryResponse } from './claim-api.service';
import { UserService } from '../../core/services/user.service';

type SortKey = 'claimNumber' | 'status' | 'priority' | 'claimedTotal' | 'modified';

@Component({
  selector: 'app-claims-list',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './claims-list.component.html',
})
export class ClaimsListComponent implements OnInit {
  claims: ClaimSummaryResponse[] = [];
  loading = false;
  saving = false;
  error = '';

  // Create form — mandatory identity only; everything else is filled on the edit page.
  showCreate = false;
  newClaimNumber = '';

  sortKey: SortKey = 'modified';
  sortDir: 'asc' | 'desc' = 'desc';

  constructor(
    private readonly api: ClaimApiService,
    private readonly router: Router,
    private readonly userService: UserService,
  ) {}

  ngOnInit(): void {
    this.loadClaims();
  }

  get username(): string {
    return this.userService.getUser() ?? 'unknown';
  }

  loadClaims(): void {
    this.loading = true;
    this.error = '';
    this.api.listClaims().subscribe({
      next: claims => {
        this.claims = this.sortClaims(claims);
        this.loading = false;
      },
      error: error => {
        this.error = `Unable to load claims. ${error.message ?? ''}`;
        this.loading = false;
      },
    });
  }

  sortBy(key: SortKey): void {
    if (this.sortKey === key) {
      this.sortDir = this.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      this.sortKey = key;
      this.sortDir = key === 'modified' || key === 'claimedTotal' ? 'desc' : 'asc';
    }
    this.claims = this.sortClaims(this.claims);
  }

  private sortClaims(claims: ClaimSummaryResponse[]): ClaimSummaryResponse[] {
    const dir = this.sortDir === 'asc' ? 1 : -1;
    const key = this.sortKey;
    return [...claims].sort((a, b) => {
      const av = a[key] ?? '';
      const bv = b[key] ?? '';
      if (av < bv) return -1 * dir;
      if (av > bv) return 1 * dir;
      return 0;
    });
  }

  // Create the claim, immediately open its first draft, and land on the edit page.
  createClaim(): void {
    if (!this.newClaimNumber.trim()) return;
    this.saving = true;
    this.error = '';
    this.api
      .createClaim({ claimNumber: this.newClaimNumber.trim(), user: this.username })
      .pipe(
        switchMap(snapshot =>
          this.api
            .openDraft(snapshot.claimId, { user: this.username })
            .pipe(switchMap(draft => [{ claimId: snapshot.claimId, draftId: draft.draftId }])),
        ),
      )
      .subscribe({
        next: ({ claimId, draftId }) => {
          this.saving = false;
          this.showCreate = false;
          this.newClaimNumber = '';
          this.router.navigate(['/claims', claimId, 'drafts', draftId]);
        },
        error: error => {
          this.error = `Unable to create claim. ${error.message ?? ''}`;
          this.saving = false;
        },
      });
  }

  // Open (or resume) a draft for an existing claim and jump to the edit page.
  editClaim(claimId: string): void {
    this.api.openDraft(claimId, { user: this.username }).subscribe({
      next: draft => this.router.navigate(['/claims', claimId, 'drafts', draft.draftId]),
      error: error => (this.error = `Unable to open draft. ${error.message ?? ''}`),
    });
  }

  statusColor(status: string | null): string {
    return (
      {
        approved: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-300',
        in_review: 'bg-blue-100 text-blue-700 dark:bg-blue-950/40 dark:text-blue-300',
        rejected: 'bg-red-100 text-red-700 dark:bg-red-950/40 dark:text-red-300',
        closed: 'bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400',
      } as Record<string, string>
    )[status ?? ''] ?? 'bg-yellow-100 text-yellow-700 dark:bg-yellow-950/40 dark:text-yellow-300';
  }

  priorityColor(priority: string | null): string {
    return (
      {
        critical: 'bg-red-100 text-red-700 dark:bg-red-950/40 dark:text-red-300',
        high: 'bg-orange-100 text-orange-700 dark:bg-orange-950/40 dark:text-orange-300',
        medium: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-950/40 dark:text-yellow-300',
        low: 'bg-blue-100 text-blue-700 dark:bg-blue-950/40 dark:text-blue-300',
      } as Record<string, string>
    )[priority ?? ''] ?? 'bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400';
  }

  label(value: string | null): string {
    return (value ?? '—').replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
  }
}
