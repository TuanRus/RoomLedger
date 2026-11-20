export const environment = {
  production: false,

  apiBaseUrl: '/api',

  cognito: {
    region: 'ap-southeast-2',
    userPoolId: 'ap-southeast-2_lstmpdxwI',
    authority: 'https://cognito-idp.ap-southeast-2.amazonaws.com/ap-southeast-2_lstmpdxwI',
    jwksUri:
      'https://cognito-idp.ap-southeast-2.amazonaws.com/ap-southeast-2_lstmpdxwI/.well-known/jwks.json',
    hostedUiDomain: 'https://auth.roomledger-dev.kiruaaaa.io.vn',
    clientId: '3sl18d6o0sutnc4fet474kfam4',
    scope: 'openid email',
    redirectUrl: 'https://roomledger-dev.kiruaaaa.io.vn/auth/callback',
    postLogoutRedirectUri: 'https://roomledger-dev.kiruaaaa.io.vn/',
  },
};
