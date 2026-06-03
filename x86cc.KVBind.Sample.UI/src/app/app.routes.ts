import { Routes } from '@angular/router';
import { AppLayoutComponent } from './shared/layout/app-layout/app-layout.component';
import { ClaimsListComponent } from './features/claims/claims-list.component';
import { ClaimDetailComponent } from './features/claims/claim-detail.component';
import { ClaimDraftComponent } from './features/claims/claim-draft.component';

export const routes: Routes = [
  {
    path: '',
    component: AppLayoutComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'claims' },
      { path: 'claims', component: ClaimsListComponent, title: 'Insurance Claims | KVBind Sample' },
      { path: 'claims/:claimId', component: ClaimDetailComponent, title: 'Claim Snapshot | KVBind Sample' },
      { path: 'claims/:claimId/drafts/:draftId', component: ClaimDraftComponent, title: 'Claim Draft | KVBind Sample' },
    ],
  },
  { path: '**', redirectTo: 'claims' },
];
