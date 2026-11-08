import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-portion-input',
  template: `<label [for]="id()">{{ label() }}</label>
    <input
      [id]="id()"
      type="number"
      inputmode="decimal"
      min="0"
      max="1000000"
      step="0.5"
      [value]="value()"
      [disabled]="disabled()"
      [attr.aria-describedby]="id() + '-hint'"
      (input)="changed($event)"
      (blur)="touched.emit()"
    />
    <p class="hint" [id]="id() + '-hint'">
      {{ allowZero() ? 'Zero is allowed.' : 'Use a positive portion count.' }} Arrows step by half a
      portion; you can type another decimal.
    </p>`,
})
export class PortionInput {
  readonly id = input.required<string>();
  readonly label = input.required<string>();
  readonly value = input.required<string>();
  readonly allowZero = input(false);
  readonly disabled = input(false);
  readonly valueChange = output<string>();
  readonly touched = output<void>();
  changed(event: Event): void {
    this.valueChange.emit((event.target as HTMLInputElement).value);
  }
}
