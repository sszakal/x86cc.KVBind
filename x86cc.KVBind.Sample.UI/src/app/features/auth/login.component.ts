import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { UserService } from '../../core/services/user.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="flex min-h-screen items-center justify-center bg-gray-50 dark:bg-gray-950">
      <div class="w-full max-w-sm">
        <div class="rounded-2xl border border-gray-200 bg-white p-8 shadow-sm dark:border-gray-800 dark:bg-white/[0.03]">
          <div class="text-center">
            <span class="inline-flex h-12 w-12 items-center justify-center rounded-xl bg-brand-50 dark:bg-brand-950/30">
              <svg class="h-6 w-6 text-brand-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"/>
              </svg>
            </span>
            <h1 class="mt-4 text-2xl font-bold text-gray-900 dark:text-white">KVBind Demo</h1>
            <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Overlay-first draft editing framework</p>
          </div>

          <form class="mt-8 space-y-4" (ngSubmit)="login()">
            <div>
              <label class="block text-sm font-medium text-gray-700 dark:text-gray-300">
                Your name
              </label>
              <input
                class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
                [(ngModel)]="username"
                name="username"
                placeholder="e.g. john@claims-ltd.com"
                autocomplete="off"
                autofocus />
              <p class="mt-1.5 text-xs text-gray-400">Used as the author on all draft edits and commits.</p>
            </div>
            <button
              type="submit"
              class="h-11 w-full rounded-lg bg-brand-500 text-sm font-semibold text-white transition-colors hover:bg-brand-600 disabled:opacity-40"
              [disabled]="!username.trim()">
              Enter demo →
            </button>
          </form>
        </div>
        <p class="mt-4 text-center text-xs text-gray-400">No real authentication — session only</p>
      </div>
    </div>
  `,
})
export class LoginComponent {
  username = '';

  constructor(private readonly userService: UserService, private readonly router: Router) {}

  login(): void {
    const name = this.username.trim();
    if (name) {
      this.userService.setUser(name);
      this.router.navigate(['/claims']);
    }
  }
}
