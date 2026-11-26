import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';

import { API_BASE_URL, isApiRequest } from '../api/api.config';
import { DevSession } from '../dev/dev-session';
import { devUserHeader } from '../dev/dev-users';

/**
 * LocalDev only (environment.localDev): sends the chosen sample person as X-Dev-User instead of a Cognito
 * ID token. The backend accepts this header only when it runs with LocalDev__Enabled=true in Development.
 */
export const devAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const user = inject(DevSession).user();
  if (!user || !isApiRequest(req.url, inject(API_BASE_URL))) return next(req);
  return next(req.clone({ setHeaders: { 'X-Dev-User': devUserHeader(user) } }));
};
