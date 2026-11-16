import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly isDark = signal(true);

  constructor() {
    // Default to dark unless the user explicitly chose light previously.
    const stored = localStorage.getItem('kvbind-theme');
    this.apply(stored !== 'light');
  }

  toggle(): void {
    this.apply(!this.isDark());
  }

  private apply(dark: boolean): void {
    this.isDark.set(dark);
    document.documentElement.classList.toggle('dark', dark);
    localStorage.setItem('kvbind-theme', dark ? 'dark' : 'light');
  }
}
