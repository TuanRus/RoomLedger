import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { switchMap } from 'rxjs';

import { API_BASE_URL } from '../api/api.config';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const oidc = inject(OidcSecurityService);
  const apiBaseUrl = inject(API_BASE_URL);

  const apiOrigin = new URL(apiBaseUrl).origin;
  const requestOrigin = new URL(req.url, apiBaseUrl).origin;

  // Only attach the ID token to RoomLedger API requests.
  if (requestOrigin !== apiOrigin) {
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
