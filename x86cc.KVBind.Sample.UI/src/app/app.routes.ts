import { Routes } from '@angular/router';
import { AppLayoutComponent } from './shared/layout/app-layout/app-layout.component';
import { ClaimsListComponent } from './features/claims/claims-list.component';
import { ClaimDetailComponent } from './features/claims/claim-detail.component';
import { ClaimDraftComponent } from './features/claims/claim-draft.component';
import { LoginComponent } from './features/auth/login.component';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: 'login', component: LoginComponent, title: 'Sign in | KVBind Demo' },
  {
    path: '',
    component: AppLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'claims' },
      { path: 'claims', component: ClaimsListComponent, title: 'Claims | KVBind Demo' },
      { path: 'claims/:claimId', component: ClaimDetailComponent, title: 'Claim | KVBind Demo' },
      { path: 'claims/:claimId/drafts/:draftId', component: ClaimDraftComponent, title: 'Draft | KVBind Demo' },
    ],
  },
  { path: '**', redirectTo: 'claims' },
];
