import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ClaimApiService, ClaimChangeSetResponse, ClaimSnapshotResponse } from './claim-api.service';

@Component({
  selector: 'app-claim-detail',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './claim-detail.component.html',
})
export class ClaimDetailComponent implements OnInit {
  claimId = '';
  snapshot: ClaimSnapshotResponse | null = null;
  changesets: ClaimChangeSetResponse[] = [];
  draftUser = 'adjuster-a';
  loading = false;
  error = '';

  constructor(private readonly api: ClaimApiService, private readonly route: ActivatedRoute, private readonly router: Router) {}

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
    this.api.listChangeSets(this.claimId).subscribe({ next: changesets => this.changesets = changesets });
  }

  openDraft(): void {
    this.api.openDraft(this.claimId, { user: this.draftUser }).subscribe({
      next: draft => this.router.navigate(['/claims', this.claimId, 'drafts', draft.draftId]),
      error: error => this.error = `Unable to open draft. ${error.message ?? ''}`,
    });
  }
}
