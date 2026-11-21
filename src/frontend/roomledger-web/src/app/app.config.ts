import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { authConfig } from './core/auth/auth.config';
import { authInterceptor } from './core/auth/auth.interceptor';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    authConfig,
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor])),
  ],
};
