import { Component, inject } from '@angular/core';
import { AsyncPipe, JsonPipe } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';

import { AuthService } from './core/auth/auth.service';

@Component({
  selector: 'app-root',
  imports: [AsyncPipe, JsonPipe, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly oidc = inject(OidcSecurityService);

  protected readonly isAuthenticated$ = this.oidc.isAuthenticated$;
  protected readonly userData$ = this.oidc.userData$;
}
