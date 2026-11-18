export const environment = {
  production: true,

  apiBaseUrl: 'https://YOUR_API_DOMAIN',

  cognito: {
    authority: 'https://YOUR_COGNITO_ISSUER',

    clientId: 'YOUR_TEAM_APP_CLIENT_ID',

    hostedUiDomain: 'https://YOUR_TEAM_COGNITO_DOMAIN',

    scope: 'openid email',
  },
};
