import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OperationContext, OperationRendererComponent } from '../operation-renderers';

// QUOTE on an underwriting application: UnderwritingApplication.Quote(QuoteRequest { BaseRate, Modifier }).
@Component({
  selector: 'app-quote-operation',
  standalone: true,
  imports: [FormsModule],
  template: `
    <span class="flex items-center gap-1">
      <input type="number" step="0.01" min="0" class="h-8 w-24 rounded-md border border-indigo-200 px-2 text-xs dark:border-indigo-900 dark:bg-gray-900 dark:text-white"
        [(ngModel)]="baseRate" placeholder="base rate" title="Base rate" />
      <input type="number" step="0.01" min="0" class="h-8 w-24 rounded-md border border-indigo-200 px-2 text-xs dark:border-indigo-900 dark:bg-gray-900 dark:text-white"
        [(ngModel)]="modifier" placeholder="modifier" title="Modifier" />
      <button class="h-8 rounded-md bg-indigo-500 px-3 text-xs font-semibold text-white hover:bg-indigo-600 disabled:opacity-40"
        [disabled]="context.busy || !valid" (click)="run()">QUOTE</button>
    </span>
  `,
})
export class QuoteOperationComponent implements OperationRendererComponent {
  @Input({ required: true }) context!: OperationContext;
  baseRate: number | null = null;
  modifier: number | null = 1;

  get valid(): boolean { return (this.baseRate ?? 0) > 0 && (this.modifier ?? 0) > 0; }

  run(): void {
    if (!this.valid) return;
    this.context.run({ baseRate: this.baseRate, modifier: this.modifier });
  }
}
