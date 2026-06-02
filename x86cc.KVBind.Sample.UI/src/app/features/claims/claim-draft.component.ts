import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { ClaimApiService, ClaimDraftResponse, ClaimPatchOperationRequest, StaleDraftResponse } from './claim-api.service';

@Component({
  selector: 'app-claim-draft',
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './claim-draft.component.html',
})
export class ClaimDraftComponent implements OnInit {
  claimId = '';
  draftId = '';
  draft: ClaimDraftResponse | null = null;
  error = '';
  staleDraft: StaleDraftResponse | null = null;
  saving = false;
  committing = false;
  itemDescription = 'Rear bumper';
  itemAmount = 1250;
  claimantType = 'PERSON';
  claimantName = 'Jane Smith';

  form = {
    description: '',
    incidentDate: '',
    policyNumber: '',
    coverageType: '',
    user: 'adjuster-a',
  };

  constructor(private readonly api: ClaimApiService, private readonly route: ActivatedRoute, private readonly router: Router) {}

  ngOnInit(): void {
    this.claimId = this.route.snapshot.paramMap.get('claimId') ?? '';
    this.draftId = this.route.snapshot.paramMap.get('draftId') ?? '';
    this.load();
  }

  load(): void {
    this.api.getDraft(this.claimId, this.draftId).subscribe({
      next: draft => this.setDraft(draft),
      error: error => this.error = `Unable to load draft. ${error.message ?? ''}`,
    });
  }

  saveFields(): void {
    const operations: ClaimPatchOperationRequest[] = [
      { operationCode: 'SET', path: '/Description', value: this.form.description },
      { operationCode: 'SET', path: '/IncidentDate', value: this.form.incidentDate },
      { operationCode: 'SET', path: '/Policy/PolicyNumber', value: this.form.policyNumber },
      { operationCode: 'SET', path: '/Policy/CoverageType', value: this.form.coverageType },
    ];
    this.patch(operations);
  }

  addDamagedItem(): void {
    const itemId = crypto.randomUUID();
    this.patch([
      { operationCode: 'ADD', path: '/DamagedItems', value: { itemId } },
      { operationCode: 'SET', path: `/DamagedItems/${itemId}/Description`, value: this.itemDescription },
      { operationCode: 'SET', path: `/DamagedItems/${itemId}/EstimatedAmount`, value: this.itemAmount },
    ]);
  }

  setClaimant(): void {
    const path = this.claimantType === 'COMPANY' ? '/Claimant/CompanyName' : '/Claimant/FullName';
    this.patch([
      { operationCode: 'INIT', path: '/Claimant', value: this.claimantType },
      { operationCode: 'SET', path, value: this.claimantName },
    ]);
  }

  commit(): void {
    this.committing = true;
    this.error = '';
    this.staleDraft = null;
    this.api.commitDraft(this.claimId, this.draftId, { user: this.form.user }).subscribe({
      next: () => this.router.navigate(['/claims', this.claimId]),
      error: error => {
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.staleDraft = error.error as StaleDraftResponse;
        } else {
          this.error = `Unable to commit draft. ${error.message ?? ''}`;
        }
        this.committing = false;
      },
    });
  }

  private patch(operations: ClaimPatchOperationRequest[]): void {
    this.saving = true;
    this.error = '';
    this.api.patchDraft(this.claimId, this.draftId, operations).subscribe({
      next: draft => {
        this.setDraft(draft);
        this.saving = false;
      },
      error: error => {
        this.error = `Unable to patch draft. ${error.message ?? ''}`;
        this.saving = false;
      },
    });
  }

  private setDraft(draft: ClaimDraftResponse): void {
    this.draft = draft;
    this.form = {
      description: draft.claim.description ?? '',
      incidentDate: draft.claim.incidentDate ?? '',
      policyNumber: draft.claim.policy.policyNumber ?? '',
      coverageType: draft.claim.policy.coverageType ?? '',
      user: draft.user,
    };
  }
}
