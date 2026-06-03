import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { ClaimApiService, ClaimSummaryResponse, CreateClaimRequest } from './claim-api.service';

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

  form: CreateClaimRequest = {
    claimNumber: `CLM-${new Date().getFullYear()}-${Math.floor(Math.random() * 9000 + 1000)}`,
    incidentDate: new Date().toISOString().slice(0, 10),
    description: 'Initial insurance claim draft',
    policyNumber: 'POL-10001',
    coverageType: 'Auto',
    user: 'adjuster-a',
  };

  constructor(private readonly api: ClaimApiService, private readonly router: Router) {}

  ngOnInit(): void {
    this.loadClaims();
  }

  loadClaims(): void {
    this.loading = true;
    this.error = '';
    this.api.listClaims().subscribe({
      next: claims => {
        this.claims = claims;
        this.loading = false;
      },
      error: error => {
        this.error = `Unable to load claims. ${error.message ?? ''}`;
        this.loading = false;
      },
    });
  }

  createClaim(): void {
    this.saving = true;
    this.error = '';
    this.api.createClaim(this.form).subscribe({
      next: response => {
        this.saving = false;
        this.router.navigate(['/claims', response.claimId]);
      },
      error: error => {
        this.error = `Unable to create claim. ${error.message ?? ''}`;
        this.saving = false;
      },
    });
  }
}
