import { Component, Input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OperationContext, OperationRendererComponent } from '../operation-renderers';

// ADD_ENDORSEMENT on the Endorsements collection: UnderwritingApplication.AddEndorsement(EndorsementRequest { Code, Description }).
@Component({
  selector: 'app-endorsement-operation',
  standalone: true,
  imports: [FormsModule],
  template: `
    <span class="flex flex-wrap items-center gap-1">
      <input class="h-8 w-28 rounded-md border border-indigo-200 px-2 text-xs dark:border-indigo-900 dark:bg-gray-900 dark:text-white"
        [(ngModel)]="code" placeholder="code" />
      <input class="h-8 w-56 rounded-md border border-indigo-200 px-2 text-xs dark:border-indigo-900 dark:bg-gray-900 dark:text-white"
        [(ngModel)]="description" placeholder="description (optional)" />
      <button class="h-8 rounded-md bg-indigo-500 px-3 text-xs font-semibold text-white hover:bg-indigo-600 disabled:opacity-40"
        [disabled]="context.busy || !code.trim()" (click)="run()">Add endorsement</button>
    </span>
  `,
})
export class EndorsementOperationComponent implements OperationRendererComponent {
  @Input({ required: true }) context!: OperationContext;
  code = '';
  description = '';

  run(): void {
    const code = this.code.trim();
    if (!code) return;
    this.context.run({ code, description: this.description.trim() || null });
    this.code = '';
    this.description = '';
  }
}
