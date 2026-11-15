import { Routes } from '@angular/router';
import { AppLayoutComponent } from './shared/layout/app-layout/app-layout.component';
import { LoginComponent } from './features/auth/login.component';
import { authGuard } from './core/guards/auth.guard';
import { ModelsHomeComponent } from './features/models/models-home.component';
import { AggregateListComponent } from './features/models/aggregate-list.component';
import { AggregateDetailComponent } from './features/models/aggregate-detail.component';
import { DraftEditorComponent } from './features/models/draft-editor.component';
import { DraftReviewComponent } from './features/models/draft-review.component';
import { DraftRebaseComponent } from './features/models/draft-rebase.component';

// Every model uses the same generic pages, driven by its schema: /m/{model}/...
export const routes: Routes = [
  { path: 'login', component: LoginComponent, title: 'Sign in | KVBind Demo' },
  {
    path: '',
    component: AppLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'models' },
      { path: 'models', component: ModelsHomeComponent, title: 'Models | KVBind Demo' },
      { path: 'm/:model', component: AggregateListComponent, title: 'List | KVBind Demo' },
      { path: 'm/:model/:id', component: AggregateDetailComponent, title: 'Details | KVBind Demo' },
      { path: 'm/:model/:id/drafts/:draftId', component: DraftEditorComponent, title: 'Draft | KVBind Demo' },
      { path: 'm/:model/:id/drafts/:draftId/review', component: DraftReviewComponent, title: 'Review changes | KVBind Demo' },
      { path: 'm/:model/:id/drafts/:draftId/rebase', component: DraftRebaseComponent, title: 'Resolve conflicts | KVBind Demo' },

      // The claim pages of earlier versions.
      { path: 'claims', pathMatch: 'full', redirectTo: 'm/claims' },
      { path: 'claims/:id', redirectTo: 'm/claims/:id' },
      { path: 'claims/:id/drafts/:draftId', redirectTo: 'm/claims/:id/drafts/:draftId' },
    ],
  },
  { path: '**', redirectTo: 'models' },
];
