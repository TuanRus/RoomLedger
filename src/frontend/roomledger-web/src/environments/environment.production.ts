export const environment = {
  production: true,

  apiBaseUrl: '/api',

  cognito: {
    authority: 'https://cognito-idp.ap-southeast-2.amazonaws.com/ap-southeast-2_lstmpdxwI',
    clientId: '3sl18d6o0sutnc4fet474kfam4',
    hostedUiDomain: 'https://auth.roomledger-dev.kiruaaaa.io.vn',
    scope: 'openid email',
    redirectUrl: 'https://roomledger-dev.kiruaaaa.io.vn/auth/callback',
    postLogoutRedirectUri: 'https://roomledger-dev.kiruaaaa.io.vn/',
  },
};
