import { provideAuth, withAppInitializerAuthCheck } from 'angular-auth-oidc-client';

import { environment } from '../../../environments/environment';

export const authConfig = provideAuth(
  {
    config: {
      authority: environment.cognito.authority,
      clientId: environment.cognito.clientId,

      redirectUrl: window.location.origin,
      postLogoutRedirectUri: window.location.origin,

      scope: environment.cognito.scope,
      responseType: 'code',

      silentRenew: true,
      useRefreshToken: true,
    },
  },
  withAppInitializerAuthCheck(),
);
