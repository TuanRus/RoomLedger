import { Component, ElementRef, computed, inject, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';

import { Viewport } from '../core/layout/viewport';
import { landlordBadges, landlordIdentity } from './landlord-shell';
import { tenantBadges, tenantIdentity } from './tenant-shell';
import { Icon } from '../shared/ui/icon';

export interface NavItem {
  path: string;
  icon: string;
  label: string;
  /** Short label for the phone bottom bar. */
  short?: string;
  /** Position in the phone bottom bar (max 5 incl. the camera button); omit to hide it there. */
  phone?: number;
  /** Rendered as the round camera button in the middle of the phone bar (landlord meter capture). */
  fab?: boolean;
}

export interface ShellConfig {
  role: 'landlord' | 'tenant';
  navLabel: string;
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
          <div class="prop"><div>{{ identity().place.title }}<small>{{ identity().place.sub }}</small></div></div>
          <nav class="nav" [attr.aria-label]="c.navLabel">
            @for (item of sideItems(); track item.path) {
              <a [routerLink]="item.path" routerLinkActive="on" ariaCurrentWhenActive="page">
                <app-icon [name]="item.icon" />{{ item.label }}
                @if (badges()[item.path]; as n) { <span class="count" aria-hidden="true">{{ n }}</span><span class="sr">, {{ n }} cần xử lý</span> }
              </a>
            }
          </nav>
          <div class="who"><span class="av" [class.tenant]="c.role === 'tenant'">{{ identity().person.initials }}</span><div>{{ identity().person.name }}<br><span class="muted">{{ identity().person.role }}</span></div></div>
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
              @if (badges()[item.path]; as n) { <span class="count" aria-hidden="true">{{ n }}</span><span class="sr">, {{ n }} cần xử lý</span> }
            </a>
          }
        }
        @if (moreItems().length) {
          <button type="button" class="more" [class.on]="moreActive()" aria-haspopup="dialog" (click)="moreDialog()?.nativeElement?.showModal()">
            <app-icon name="i-more" />Thêm
            @if (moreWaiting()) { <span class="dot" aria-hidden="true"></span><span class="sr">, có mục cần xử lý</span> }
          </button>
        }
      </nav>
      @if (moreItems().length) {
        <dialog #more class="more-sheet" aria-labelledby="more-t" (click)="closeOnBackdrop($event)">
          <div class="head">
            <h2 id="more-t">Thêm</h2>
            <button type="button" class="x" aria-label="Đóng" (click)="more.close()"><app-icon name="i-x" /></button>
          </div>
          <nav [attr.aria-label]="'Mục khác · ' + c.navLabel">
            @for (item of moreItems(); track item.path) {
              <a [routerLink]="item.path" routerLinkActive="on" ariaCurrentWhenActive="page" (click)="more.close()">
                <app-icon [name]="item.icon" /><span class="lbl">{{ item.label }}</span>
                @if (badges()[item.path]; as n) { <span class="count" aria-hidden="true">{{ n }}</span><span class="sr">, {{ n }} cần xử lý</span> }
                <app-icon name="i-chev" />
              </a>
            }
          </nav>
          <p class="small muted who-line">{{ identity().person.name }} · {{ identity().place.title }}</p>
        </dialog>
      }
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
    .bnav .more { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 4px; min-height: 48px;
      border: 0; background: none; padding: 0; font: 500 13px/1.2 var(--sans); color: var(--ink-2); cursor: pointer; }
    .bnav .more.on { color: var(--ink); }
    .bnav .more { position: relative; }
    .bnav .more .dot { position: absolute; top: 6px; left: calc(50% + 8px); width: 10px; height: 10px; border-radius: 50%;
      background: var(--warn); box-shadow: 0 0 0 2px var(--surface); }
    .more-sheet { margin: auto 0 0; width: 100%; max-width: 100%; border: 0; border-radius: 24px 24px 0 0; color: var(--ink);
      background: var(--surface); padding: 12px 16px calc(20px + env(safe-area-inset-bottom)); box-shadow: 0 -20px 60px -20px rgb(0 0 0 / .35); }
    .more-sheet::backdrop { background: rgb(20 19 17 / .5); }
    .more-sheet .head { display: flex; align-items: center; justify-content: space-between; padding: 4px 0 8px; }
    .more-sheet h2 { font-size: var(--t-20); letter-spacing: -.02em; margin: 0; }
    .more-sheet .x { width: 44px; height: 44px; margin-right: -10px; border: 0; background: none; border-radius: 12px; display: grid; place-items: center; color: var(--ink-2); cursor: pointer; }
    .more-sheet nav { display: grid; }
    .more-sheet nav a { display: flex; align-items: center; gap: 14px; min-height: 56px; padding: 0 4px; border-bottom: 1px solid var(--line);
      color: var(--ink); text-decoration: none; font-size: var(--t-16); }
    .more-sheet nav a.on { font-weight: 600; }
    .more-sheet .count { min-width: 22px; height: 22px; border-radius: 11px; padding: 0 7px; background: var(--warn); color: #fff; font: 600 13px/22px var(--sans); text-align: center; }
    .more-sheet .lbl { flex: 1; }
    .more-sheet nav a > svg.i:last-child { color: var(--ink-3); }
    .more-sheet .who-line { margin: 14px 4px 0; }
  `,
})
export class AppShell {
  protected readonly viewport = inject(Viewport);
  protected readonly config = toSignal(inject(ActivatedRoute).data.pipe(map((d) => d['shell'] as ShellConfig)), {
    requireSync: true,
  });
  protected readonly sideItems = computed(() => this.config().nav);
  protected readonly phoneItems = computed(() => this.config().nav.filter((n) => n.phone != null).sort((a, b) => a.phone! - b.phone!));
  /** Items without a phone slot, shown in the "Thêm" sheet when the bar has room for that button. */
  protected readonly moreItems = computed(() => (this.phoneItems().length < 5 ? this.config().nav.filter((n) => n.phone == null) : []));
  /** Live counts of what is waiting on this person, per nav path (0 hides the badge). */
  protected readonly badges = this.config().role === 'landlord' ? landlordBadges() : tenantBadges();
  /** Sidebar place and person: sample text with sample data, /me and the property or tenant home with the API. */
  protected readonly identity = this.config().role === 'landlord' ? landlordIdentity() : tenantIdentity();
  /** The "Thêm" button gets a dot when anything inside its sheet is waiting. */
  protected readonly moreWaiting = computed(() => this.moreItems().some((n) => (this.badges()[n.path] ?? 0) > 0));
  protected readonly moreDialog = viewChild<ElementRef<HTMLDialogElement>>('more');

  private readonly router = inject(Router);
  private readonly url = toSignal(this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)), {
    initialValue: this.router.url,
  });
  /** "Thêm" is lit when the current page is one of its items, e.g. /landlord/disputes. */
  protected readonly moreActive = computed(() => {
    const segment = this.url().split(/[?#]/)[0].split('/')[2];
    return this.moreItems().some((n) => n.path === segment);
  });

  /** A click on the dialog element itself (not its content) is a click on the backdrop. */
  protected closeOnBackdrop(event: MouseEvent): void {
    const dialog = event.currentTarget as HTMLDialogElement;
    if (event.target === dialog) dialog.close();
  }
}
