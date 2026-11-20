import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { take } from 'rxjs';

@Component({
  selector: 'app-auth-callback',
  standalone: true,
  template: `<p>Completing sign-in...</p>`,
})
export class AuthCallbackComponent implements OnInit {
  private readonly oidc = inject(OidcSecurityService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.oidc.isAuthenticated$.pipe(take(1)).subscribe(() => {
      this.router.navigateByUrl('/', { replaceUrl: true });
    });
  }
}
