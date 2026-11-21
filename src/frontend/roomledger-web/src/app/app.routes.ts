import { Routes } from '@angular/router';

import { AuthCallbackComponent } from './core/auth/auth-callback.component';
import { HomeComponent } from './features/home/home';

export const routes: Routes = [
  { path: 'auth/callback', component: AuthCallbackComponent },
  { path: '', component: HomeComponent },
];
