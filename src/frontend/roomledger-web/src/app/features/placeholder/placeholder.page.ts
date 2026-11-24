import { Component, input } from '@angular/core';

/** Stand-in for screens not built yet; names the design frame to build from. */
@Component({
  selector: 'app-placeholder-page',
  template: `
    <div class="top"><div><h1>{{ title() }}</h1><p class="sub">Màn này đang được xây dựng theo thiết kế {{ frames() }}.</p></div></div>
  `,
})
export class PlaceholderPage {
  /** From route data. */
  readonly title = input('Đang xây dựng');
  readonly frames = input('');
}
