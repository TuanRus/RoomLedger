import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { switchMap } from 'rxjs';

import { API_BASE_URL, isApiRequest } from '../api/api.config';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const oidc = inject(OidcSecurityService);

  // Only attach the ID token to RoomLedger API requests.
  if (!isApiRequest(req.url, inject(API_BASE_URL))) {
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
