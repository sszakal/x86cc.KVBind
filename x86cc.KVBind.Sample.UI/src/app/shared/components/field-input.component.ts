import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppSelectComponent } from './app-select.component';

export interface FieldMeta {
  key: string;
  label: string;
  dataType: string;
  uiHint: string;        // text | textarea | select | radio | number | date
  isRequired: boolean;
  allowedValues: string[] | null;
}

@Component({
  selector: 'app-field-input',
  standalone: true,
  imports: [CommonModule, FormsModule, AppSelectComponent],
  template: `
    <label class="block">
      <span class="text-sm font-medium text-gray-700 dark:text-gray-300">
        {{ field.label }}
        @if (field.isRequired) { <span class="text-red-500">*</span> }
      </span>

      @switch (field.uiHint) {

        <!-- Textarea -->
        @case ('textarea') {
          <textarea
            class="mt-1.5 min-h-[80px] w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            [ngModel]="strVal"
            (ngModelChange)="emit($event)"
            [placeholder]="field.label">
          </textarea>
        }

        <!-- Select (AllowedValues with many options) -->
        @case ('select') {
          <div class="mt-1.5">
            <app-select
              [options]="selectOptions"
              [value]="strVal"
              [placeholder]="'Select ' + field.label.toLowerCase() + '…'"
              (valueChange)="emit($event)">
            </app-select>
          </div>
        }

        <!-- Radio (AllowedValues with few options) -->
        @case ('radio') {
          <div class="mt-2 flex flex-wrap gap-2">
            @for (v of field.allowedValues ?? []; track v) {
              <label class="flex cursor-pointer items-center gap-2 rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                [ngClass]="strVal === v
                  ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300 border-transparent'
                  : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800'">
                <input type="radio" class="sr-only" [name]="field.key" [value]="v"
                  [ngModel]="strVal" (ngModelChange)="emit($event)" />
                {{ label(v) }}
              </label>
            }
            <label class="flex cursor-pointer items-center rounded-lg border border-dashed border-gray-200 px-3 py-2 text-xs text-gray-400 dark:border-gray-700">
              <input type="radio" class="sr-only" [name]="field.key" [value]="null"
                [ngModel]="strVal" (ngModelChange)="emit(null)" />
              Clear
            </label>
          </div>
        }

        <!-- Date -->
        @case ('date') {
          <input type="date"
            class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            [ngModel]="strVal"
            (ngModelChange)="emit($event)" />
        }

        <!-- Number -->
        @case ('number') {
          <input type="number"
            class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            [ngModel]="numVal"
            (ngModelChange)="emit($event)"
            step="0.01" />
        }

        <!-- Default: text -->
        @default {
          <input type="text"
            class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            [ngModel]="strVal"
            (ngModelChange)="emit($event)"
            [placeholder]="field.label" />
        }

      }
    </label>
  `,
})
export class FieldInputComponent implements OnChanges {
  @Input() field!: FieldMeta;
  @Input() value: unknown = null;
  @Output() valueChange = new EventEmitter<unknown>();

  strVal: string | null = null;
  numVal: number | null = null;
  selectOptions: { value: string; label: string }[] = [];

  ngOnChanges(): void {
    this.strVal = this.value != null ? String(this.value) : null;
    this.numVal = this.value != null ? Number(this.value) : null;
    this.selectOptions = (this.field.allowedValues ?? []).map(v => ({ value: v, label: this.label(v) }));
  }

  emit(val: unknown): void {
    const coerced = val == null || val === ''
      ? null
      : this.field.dataType === 'decimal' || this.field.dataType === 'int'
        ? Number(val)
        : val;
    this.valueChange.emit(coerced);
  }

  label(v: string): string {
    return v.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
  }
}
