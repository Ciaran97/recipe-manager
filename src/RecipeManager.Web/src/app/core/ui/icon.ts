import { Component, input } from '@angular/core';

const paths = {
  cookbook:
    'M6 3h14v18H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3Zm0 0v14h14M6 19h14M10 13c-1-4 1-7 7-7 1 5-2 8-7 7Zm0 0 4-4',
  book: 'M3 4h7c1 0 2 1 2 2v15c0-2-2-3-4-3H3V4Zm18 0h-7c-1 0-2 1-2 2v15c0-2 2-3 4-3h5V4Z',
  heart:
    'M20.8 4.6a5.5 5.5 0 0 0-7.8 0L12 5.7l-1.1-1.1a5.5 5.5 0 0 0-7.8 7.8L12 21l8.8-8.6a5.5 5.5 0 0 0 0-7.8Z',
  calendar: 'M4 5h16v16H4V5Zm3-3v6m10-6v6M4 11h16M8 15h1m6 0h1m-8 3h1m6 0h1',
  list: 'M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01',
  settings: 'M4 7h16M4 17h16M8 4v6m8 4v6',
  leaf: 'M20 3c-9-1-16 3-16 10 0 4 4 7 8 5 6-2 7-9 8-15ZM3 22 15 9',
  plus: 'M12 5v14M5 12h14',
  search: 'M21 21l-5-5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z',
  sun: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm0-6v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5m11 11L19 19M5 19l1.5-1.5m11-11L19 5',
  moon: 'M21 13A9 9 0 0 1 11 3a9 9 0 1 0 10 10Z',
  menu: 'M3 6h18M3 12h18M3 18h18',
  close: 'M6 6l12 12M18 6 6 18',
  clock: 'M12 7v5l3 2m6-2a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z',
  servings:
    'M8 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM2 21v-3c0-3 3-5 6-5s6 2 6 5v3m2-17a4 4 0 0 1 0 8m1 2c3 0 5 2 5 5v2',
  dish: 'M3 17h18M5 17a7 7 0 0 1 14 0M12 7V4m-2 0h4M4 21h16',
  arrow: 'M19 12H5m6-6-6 6 6 6',
} as const;
export type IconName = keyof typeof paths;
@Component({
  selector: 'app-icon',
  template: `<svg
    viewBox="0 0 24 24"
    aria-hidden="true"
    focusable="false"
    [attr.fill]="filled() ? 'currentColor' : 'none'"
    stroke="currentColor"
    stroke-width="1.7"
    stroke-linecap="round"
    stroke-linejoin="round"
  >
    <path [attr.d]="paths[name()]" />
  </svg>`,
  styles: `
    :host {
      display: inline-flex;
      width: 1.25em;
      height: 1.25em;
      flex-shrink: 0;
    }
    svg {
      display: block;
      width: 100%;
      height: 100%;
    }
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly filled = input(false);
  readonly paths = paths;
}
