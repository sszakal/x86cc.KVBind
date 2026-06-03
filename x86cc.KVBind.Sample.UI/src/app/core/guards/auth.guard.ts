import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { UserService } from '../services/user.service';

export const authGuard: CanActivateFn = () => {
  const user = inject(UserService);
  const router = inject(Router);
  if (user.isLoggedIn()) return true;
  return router.createUrlTree(['/login']);
};
