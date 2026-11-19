import { Injectable, inject } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';

import { environment } from '../../../environments/environment';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);

  login(): void {
    this.oidc.authorize();
  }

  logout(): void {
    this.oidc.logoffLocal();

    const params = new URLSearchParams({
      client_id: environment.cognito.clientId,
      logout_uri: window.location.origin,
    });

    window.location.assign(`${environment.cognito.hostedUiDomain}/logout?${params.toString()}`);
  }
}
