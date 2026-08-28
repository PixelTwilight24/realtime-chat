import { CanActivateFn, Router } from '@angular/router';
import { inject } from '@angular/core';
import { Auth } from '../services/auth';

export const authGuard: CanActivateFn = () => {
  if (inject(Auth).isAuthenticated()) return true;

  return inject(Router).createUrlTree(['/login']);
};
