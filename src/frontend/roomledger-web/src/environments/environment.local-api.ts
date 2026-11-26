import { Environment } from './environment.model';

/**
 * `pnpm start:local-api`: the screens call the backend running on this machine (LocalDev mode).
 * Requests go to /api/v1 on the dev server and proxy.local-api.json forwards them to the API,
 * so no CORS setting is needed. Cognito is not used here; sign-in is the role picker at /preview.
 */
export const environment: Environment = {
  production: false,

  apiBaseUrl: '/api/v1',

  dataSource: 'api',
  localDev: true,

  cognito: { authority: '', clientId: '', hostedUiDomain: '', scope: 'openid email' },
};
