import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class UserService {
  private readonly key = 'kvbind-user';

  getUser(): string | null {
    return sessionStorage.getItem(this.key);
  }

  setUser(username: string): void {
    sessionStorage.setItem(this.key, username);
  }

  clearUser(): void {
    sessionStorage.removeItem(this.key);
  }

  isLoggedIn(): boolean {
    return !!this.getUser();
  }
}
