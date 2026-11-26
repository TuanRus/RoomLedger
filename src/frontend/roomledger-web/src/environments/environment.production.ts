import { Environment } from './environment.model';

export const environment: Environment = {
  production: true,

  apiBaseUrl: 'https://YOUR_API_DOMAIN/api/v1',

  dataSource: 'api',
  localDev: false,

  cognito: {
    authority: 'https://YOUR_COGNITO_ISSUER',

    clientId: 'YOUR_TEAM_APP_CLIENT_ID',

    hostedUiDomain: 'https://YOUR_TEAM_COGNITO_DOMAIN',

    scope: 'openid email',
  },
};
