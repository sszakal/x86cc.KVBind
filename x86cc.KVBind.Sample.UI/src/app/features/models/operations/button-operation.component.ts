import { Component, Input } from '@angular/core';
import { OperationContext, OperationRendererComponent } from './operation-renderers';

// For an operation without a payload: one button. Options: `label` (defaults to the operation name),
// `confirm` (a question to confirm first).
@Component({
  selector: 'app-button-operation',
  standalone: true,
  template: `
    <button class="h-8 rounded-md bg-indigo-500 px-3 text-xs font-semibold text-white hover:bg-indigo-600 disabled:opacity-40"
      [disabled]="context.busy" (click)="run()">{{ label }}</button>
  `,
})
export class ButtonOperationComponent implements OperationRendererComponent {
  @Input({ required: true }) context!: OperationContext;

  get label(): string { return (this.context.options['label'] as string | undefined) ?? this.context.operation; }

  run(): void {
    const question = this.context.options['confirm'] as string | undefined;
    if (question && !confirm(question)) return;
    this.context.run();
  }
}
