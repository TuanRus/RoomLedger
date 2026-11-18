import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { authConfig } from './core/auth/auth.config';
import { authInterceptor } from './core/auth/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [authConfig, provideHttpClient(withInterceptors([authInterceptor]))],
};
