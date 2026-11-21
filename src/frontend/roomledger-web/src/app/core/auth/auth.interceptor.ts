import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { switchMap } from 'rxjs';

import { API_BASE_URL } from '../api/api.config';
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const oidc = inject(OidcSecurityService);
  const apiBaseUrl = inject(API_BASE_URL);

  // Resolve relative API URLs against the current application origin.
  const apiUrl = new URL(apiBaseUrl, window.location.origin);
  const apiOrigin = apiUrl.origin;
  const apiPath = apiUrl.pathname.replace(/\/$/, '');
  const requestUrl = new URL(req.url, window.location.origin);

  if (
    requestUrl.origin !== apiOrigin ||
    (!requestUrl.pathname.startsWith(`${apiPath}/`) && requestUrl.pathname !== apiPath)
  ) {
    return next(req);
  }

  return oidc.getIdToken().pipe(
    switchMap((idToken) => {
      if (!idToken) {
        return next(req);
      }

      return next(
        req.clone({
          setHeaders: {
            Authorization: `Bearer ${idToken}`,
          },
        }),
      );
    }),
  );
};
