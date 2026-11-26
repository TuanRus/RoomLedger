import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { environment } from '../environments/environment';
import { routes } from './app.routes';
import { authConfig } from './core/auth/auth.config';
import { authInterceptor } from './core/auth/auth.interceptor';
import { devAuthInterceptor } from './core/auth/dev-auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes, withComponentInputBinding()),
    // LocalDev (pnpm start:local-api) signs in as a sample person through X-Dev-User; every other build uses Cognito.
    ...(environment.localDev
      ? [provideHttpClient(withInterceptors([devAuthInterceptor]))]
      : [authConfig, provideHttpClient(withInterceptors([authInterceptor]))]),
  ],
};
