import { provideAuth, withAppInitializerAuthCheck } from 'angular-auth-oidc-client';

import { environment } from '../../../environments/environment';

const postLogoutRedirectUri =
  (environment.cognito as { postLogoutRedirectUri?: string; postLogoutRedirectUrl?: string })
    .postLogoutRedirectUri ?? window.location.origin;

export const authConfig = provideAuth(
  {
    config: {
      authority: environment.cognito.authority,
      clientId: environment.cognito.clientId,

      redirectUrl: environment.cognito.redirectUrl,
      postLogoutRedirectUri,
      postLoginRoute: '/',

      scope: environment.cognito.scope,
      responseType: 'code',

      silentRenew: true,
      useRefreshToken: true,
    },
  },
  withAppInitializerAuthCheck(),
);
