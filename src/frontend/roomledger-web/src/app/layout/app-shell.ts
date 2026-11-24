import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';

import { Viewport } from '../core/layout/viewport';
import { Icon } from '../shared/ui/icon';

export interface NavItem {
  path: string;
  icon: string;
  label: string;
  /** Short label for the phone bottom bar. */
  short?: string;
  badge?: string;
  /** Position in the phone bottom bar (max 5 incl. the camera button); omit to hide it there. */
  phone?: number;
  /** Rendered as the round camera button in the middle of the phone bar (landlord meter capture). */
  fab?: boolean;
}

export interface ShellConfig {
  role: 'landlord' | 'tenant';
  navLabel: string;
  place: { title: string; sub: string };
  person: { initials: string; name: string; role: string };
  nav: NavItem[];
}

/**
 * One responsive shell for both roles: sidebar on a computer, bottom bar on a phone
 * (PRODUCT.md: one web app that scales down to the phone UI).
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  template: `
    @let c = config();
    @if (!viewport.isPhone()) {
      <div class="app">
        <aside class="side">
          <div class="brand"><span class="logo"><app-icon name="i-meter" /></span>RoomLedger</div>
          <div class="prop"><div>{{ c.place.title }}<small>{{ c.place.sub }}</small></div></div>
          <nav class="nav" [attr.aria-label]="c.navLabel">
            @for (item of sideItems(); track item.path) {
              <a [routerLink]="item.path" routerLinkActive="on" ariaCurrentWhenActive="page">
                <app-icon [name]="item.icon" />{{ item.label }}
                @if (item.badge) { <span class="count">{{ item.badge }}</span> }
              </a>
            }
          </nav>
          <div class="who"><span class="av" [class.tenant]="c.role === 'tenant'">{{ c.person.initials }}</span><div>{{ c.person.name }}<br><span class="muted">{{ c.person.role }}</span></div></div>
        </aside>
        <main class="main"><router-outlet /></main>
      </div>
    } @else {
      <main class="phone-main"><router-outlet /></main>
      <nav class="bnav" [attr.aria-label]="c.navLabel">
        @for (item of phoneItems(); track item.path) {
          @if (item.fab) {
            <a class="cam" [routerLink]="item.path" [attr.aria-label]="item.label"><span class="fab"><app-icon [name]="item.icon" /></span></a>
          } @else {
            <a [routerLink]="item.path" routerLinkActive="on" ariaCurrentWhenActive="page">
              <app-icon [name]="item.icon" />{{ item.short ?? item.label }}
              @if (item.badge) { <span class="count">{{ item.badge }}</span> }
            </a>
          }
        }
      </nav>
    }
  `,
  styles: `
    .side { position: sticky; top: 0; height: 100vh; }
    .av.tenant { background: var(--warn); }
    .phone-main { padding: 8px 16px 112px; max-width: 560px; margin: 0 auto; }
    .bnav { position: fixed; left: 0; right: 0; bottom: 0; z-index: 10; padding-bottom: env(safe-area-inset-bottom); }
    .bnav a { position: relative; }
    .bnav .count { position: absolute; top: 2px; left: calc(50% + 6px); min-width: 20px; height: 20px; border-radius: 10px;
      padding: 0 6px; background: var(--warn); color: #fff; font: 600 13px/20px var(--sans); text-align: center; }
  `,
})
export class AppShell {
  protected readonly viewport = inject(Viewport);
  protected readonly config = toSignal(inject(ActivatedRoute).data.pipe(map((d) => d['shell'] as ShellConfig)), {
    requireSync: true,
  });
  protected readonly sideItems = computed(() => this.config().nav.filter((n) => !n.fab));
  protected readonly phoneItems = computed(() => this.config().nav.filter((n) => n.phone != null).sort((a, b) => a.phone! - b.phone!));
}
