import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ChangeTreeChange, ChangeTreeTableComponent } from './change-tree-table.component';
import { ClaimApiService, ClaimChangeSetResponse, ClaimSnapshotResponse } from './claim-api.service';
import { UserService } from '../../core/services/user.service';

@Component({
  selector: 'app-claim-detail',
  imports: [CommonModule, FormsModule, RouterModule, ChangeTreeTableComponent],
  templateUrl: './claim-detail.component.html',
})
export class ClaimDetailComponent implements OnInit {
  claimId = '';
  snapshot: ClaimSnapshotResponse | null = null;
  changesets: ClaimChangeSetResponse[] = [];
  activeTab: 'claim' | 'audit' = 'claim';
  selectedChangesetId = '';
  loading = false;
  error = '';

  constructor(
    private readonly api: ClaimApiService,
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly userService: UserService,
  ) {}

  ngOnInit(): void {
    this.claimId = this.route.snapshot.paramMap.get('claimId') ?? '';
    this.load();
  }

  load(): void {
    this.loading = true;
    this.error = '';
    this.api.getSnapshot(this.claimId).subscribe({
      next: snapshot => {
        this.snapshot = snapshot;
        this.loading = false;
      },
      error: error => {
        this.error = `Unable to load snapshot. ${error.message ?? ''}`;
        this.loading = false;
      },
    });
    this.api.listChangeSets(this.claimId).subscribe({
      next: changesets => {
        this.changesets = changesets;
        this.selectedChangesetId = changesets[0]?.commitId ?? '';
      }
    });
  }

  openDraft(): void {
    this.api.openDraft(this.claimId, { user: this.userService.getUser() ?? 'unknown' }).subscribe({
      next: draft => this.router.navigate(['/claims', this.claimId, 'drafts', draft.draftId]),
      error: error => this.error = `Unable to open draft. ${error.message ?? ''}`,
    });
  }

  changesetChanges(changeset: ClaimChangeSetResponse): ChangeTreeChange[] {
    return changeset.changes;
  }

  selectedChangeset(): ClaimChangeSetResponse | null {
    return this.changesets.find(changeset => changeset.commitId === this.selectedChangesetId) ?? this.changesets[0] ?? null;
  }

  changeSummary(changeset: ClaimChangeSetResponse): string {
    const paths = changeset.changes.map(change => change.path).slice(0, 4);
    const suffix = changeset.changes.length > paths.length ? ` +${changeset.changes.length - paths.length} more` : '';
    return paths.join(', ') + suffix || 'No changed paths';
  }
}
