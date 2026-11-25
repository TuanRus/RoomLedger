import { Component, ElementRef, input, linkedSignal, model, viewChild } from '@angular/core';
import { FormValueControl } from '@angular/forms/signals';

import { Icon } from './icon';

/** "2026-12-01" -> "01/12/2026"; anything else -> "". */
function toText(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso ?? '');
  return m ? `${m[3]}/${m[2]}/${m[1]}` : '';
}

/** "01/12/2026" -> "2026-12-01" when it is a real calendar day; otherwise "". */
function toIso(text: string): string {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(text);
  if (!m) return '';
  const [d, mo, y] = [+m[1], +m[2], +m[3]];
  const date = new Date(Date.UTC(y, mo - 1, d));
  return date.getUTCFullYear() === y && date.getUTCMonth() === mo - 1 && date.getUTCDate() === d
    ? `${m[3]}-${m[2]}-${m[1]}`
    : '';
}

/** Digits typed so far, with the slashes put in: "011220" -> "01/12/20". */
function mask(raw: string): string {
  const digits = raw.replace(/\D/g, '').slice(0, 8);
  return [digits.slice(0, 2), digits.slice(2, 4), digits.slice(4)].filter(Boolean).join('/');
}

/**
 * A date typed as dd/mm/yyyy, the Vietnamese order, whatever language the browser runs in. The native
 * `type="date"` shows mm/dd in an English browser, which turns 01/12 (1 December) into 12/01 on screen.
 * The calendar button still opens the browser's picker. Bind it with [formField]; the value is "YYYY-MM-DD".
 */
@Component({
  selector: 'app-date-field',
  imports: [Icon],
  template: `
    <div class="input df">
      <input
        class="bare"
        [id]="inputId()"
        inputmode="numeric"
        autocomplete="off"
        placeholder="dd/mm/yyyy"
        maxlength="10"
        [value]="text()"
        [attr.aria-describedby]="describedBy()"
        (input)="onInput($event)"
        (blur)="touched.set(true)"
      />
      <button type="button" class="pick" aria-label="Chọn ngày trên lịch" (click)="openPicker()">
        <app-icon name="i-cal" />
      </button>
      <input
        #native
        type="date"
        class="native"
        tabindex="-1"
        aria-hidden="true"
        [value]="value()"
        (change)="onPick($event)"
      />
    </div>
  `,
  styles: `
    /* min-width 0: inside a grid cell the field must shrink to its column, not push into the next one. */
    :host {
      display: block;
      min-width: 0;
    }
    :host(.is-error) .df {
      border-color: var(--err);
      box-shadow: 0 0 0 3px rgb(158 47 27 / 0.12);
    }
    .df {
      position: relative;
      padding-right: 4px;
    }
    .bare {
      flex: 1;
      width: 0;
      min-width: 0;
      border: 0;
      outline: none;
      background: transparent;
      font: inherit;
      color: inherit;
      font-variant-numeric: tabular-nums;
    }
    .df:focus-within {
      outline: 2px solid var(--focus);
      outline-offset: 2px;
    }
    .pick {
      width: 40px;
      height: 36px;
      border: 0;
      border-radius: 8px;
      background: none;
      color: var(--ink-2);
      display: grid;
      place-items: center;
      cursor: pointer;
    }
    .pick:hover {
      background: var(--soft);
    }
    .pick:focus-visible {
      outline: 2px solid var(--focus);
    }
    .native {
      position: absolute;
      right: 0;
      bottom: 0;
      width: 1px;
      height: 1px;
      opacity: 0;
      pointer-events: none;
    }
  `,
})
export class DateField implements FormValueControl<string> {
  /** "YYYY-MM-DD", or "" while the typed text is not a complete real date. */
  readonly value = model('');
  readonly touched = model(false);
  /** id of the text input, for the page's <label for>. */
  readonly inputId = input.required<string>();
  readonly describedBy = input<string | null>(null);

  private readonly native = viewChild.required<ElementRef<HTMLInputElement>>('native');

  /** What the user sees; keeps a half-typed date instead of wiping it when the value is still empty. */
  protected readonly text = linkedSignal<string, string>({
    source: this.value,
    computation: (iso, previous) =>
      previous && toIso(previous.value) === iso ? previous.value : toText(iso),
  });

  protected onInput(event: Event): void {
    const el = event.target as HTMLInputElement;
    const masked = mask(el.value);
    el.value = masked;
    this.text.set(masked);
    this.value.set(toIso(masked));
  }

  protected onPick(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
    this.touched.set(true);
  }

  protected openPicker(): void {
    const el = this.native().nativeElement;
    if (typeof el.showPicker === 'function') el.showPicker();
    else el.click();
  }
}
