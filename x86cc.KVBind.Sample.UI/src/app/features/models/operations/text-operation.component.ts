import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OperationContext, OperationRendererComponent } from './operation-renderers';

// For an operation whose payload is one string (e.g. a reason). Options: `label`, `placeholder`.
@Component({
  selector: 'app-text-operation',
  standalone: true,
  imports: [FormsModule],
  template: `
    <span class="flex items-center gap-1">
      <input class="h-8 w-44 rounded-md border border-indigo-200 px-2 text-xs dark:border-indigo-900 dark:bg-gray-900 dark:text-white"
        [(ngModel)]="text" [placeholder]="placeholder" (keydown.enter)="run()" />
      <button class="h-8 rounded-md bg-indigo-500 px-3 text-xs font-semibold text-white hover:bg-indigo-600 disabled:opacity-40"
        [disabled]="context.busy || !text.trim()" (click)="run()">{{ label }}</button>
    </span>
  `,
})
export class TextOperationComponent implements OperationRendererComponent {
  @Input({ required: true }) context!: OperationContext;
  text = '';

  get label(): string { return (this.context.options['label'] as string | undefined) ?? this.context.operation; }
  get placeholder(): string { return (this.context.options['placeholder'] as string | undefined) ?? ''; }

  run(): void {
    const value = this.text.trim();
    if (!value || this.context.busy) return;
    this.context.run(value);
    this.text = '';
  }
}
