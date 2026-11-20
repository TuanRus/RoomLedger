export const environment = {
  production: false,

  apiBaseUrl: 'http://localhost:5000',

  cognito: {
    authority: 'https://cognito-idp.ap-southeast-2.amazonaws.com/ap-southeast-2_BK6jzrSDY',

    clientId: '7odedlbgg49qnr71e2hjq4cpdk',

    hostedUiDomain: 'https://ap-southeast-2bk6jzrsdy.auth.ap-southeast-2.amazoncognito.com',

    scope: 'openid email',
  },
};
