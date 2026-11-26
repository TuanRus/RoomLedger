/** Shape shared by every environment file, so a file replacement cannot drop a setting. */
export interface Environment {
  production: boolean;
  /** Absolute URL, or a path served through the dev-server proxy (see proxy.local-api.json). */
  apiBaseUrl: string;
  /** 'sample': screens use the sample data in src/app/core/data; 'api': the stores call the backend. */
  dataSource: 'sample' | 'api';
  /**
   * Sign in with the backend's LocalDev header (X-Dev-User) instead of Cognito. Only for the API running on
   * your own machine with LocalDev__Enabled=true; never true in a deployed build.
   */
  localDev: boolean;
  cognito: {
    authority: string;
    clientId: string;
    hostedUiDomain: string;
    scope: string;
  };
}
