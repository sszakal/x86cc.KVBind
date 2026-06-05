import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppSelectComponent } from './app-select.component';

export interface FieldMeta {
  key: string;
  label: string;
  dataType: string;
  uiHint: string;
  isRequired: boolean;
  allowedValues: Array<{
    id: string;
    label: string;
    template: string | null;
    placeholders: Array<{ name: string; label: string; dataType: string }> | null;
  }> | null;
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

        @case ('textarea') {
          <textarea
            class="mt-1.5 min-h-[80px] w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            [ngModel]="strVal"
            (ngModelChange)="emit($event)"
            [placeholder]="field.label">
          </textarea>
        }

        @case ('select') {
          <div class="mt-1.5">
            <app-select
              [options]="selectOptions"
              [value]="strVal"
              [placeholder]="'Select ' + field.label.toLowerCase() + '…'"
              (valueChange)="onSelectChange($event)">
            </app-select>
            <!-- Template description for AllowedValueComponent -->
            @if (activeTemplate) {
              <p class="mt-1.5 rounded-lg bg-blue-50 px-3 py-2 text-xs text-blue-700 dark:bg-blue-950/30 dark:text-blue-300">
                <span class="font-medium">Template: </span>{{ activeTemplate }}
              </p>
            }
          </div>
        }

        @case ('radio') {
          <div class="mt-2 flex flex-wrap gap-2">
            @for (opt of field.allowedValues ?? []; track opt.id) {
              <label class="flex cursor-pointer items-center gap-2 rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                [ngClass]="strVal === opt.id
                  ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300 border-transparent'
                  : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800'">
                <input type="radio" class="sr-only" [name]="field.key" [value]="opt.id"
                  [ngModel]="strVal" (ngModelChange)="emit($event)" />
                {{ opt.label }}
              </label>
            }
            <label class="flex cursor-pointer items-center rounded-lg border border-dashed border-gray-200 px-3 py-2 text-xs text-gray-400 dark:border-gray-700">
              <input type="radio" class="sr-only" [name]="field.key" [value]="null"
                [ngModel]="strVal" (ngModelChange)="emit(null)" />
              Clear
            </label>
          </div>
        }

        @case ('multiselect') {
          <div class="mt-2">
            <div class="flex flex-wrap gap-2">
              @for (opt of field.allowedValues ?? []; track opt.id) {
                <label class="flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors"
                  [ngClass]="isSelected(opt.id)
                    ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300'
                    : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800'">
                  <input type="checkbox" class="sr-only" [checked]="isSelected(opt.id)"
                    (change)="toggleMulti(opt.id)" />
                  @if (isSelected(opt.id)) {
                    <svg class="h-3.5 w-3.5 text-brand-500" fill="currentColor" viewBox="0 0 20 20">
                      <path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/>
                    </svg>
                  }
                  {{ opt.label }}
                </label>
              }
            </div>
            @if (selectedLabels.length > 0) {
              <p class="mt-1.5 text-xs text-gray-500">Selected: {{ selectedLabels.join(', ') }}</p>
            }
          </div>
        }

        @case ('date') {
          <input type="date"
            class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            [ngModel]="strVal"
            (ngModelChange)="emit($event)" />
        }

        @case ('number') {
          <input type="number"
            class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
            [ngModel]="numVal"
            (ngModelChange)="emit($event)"
            step="0.01" />
        }

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
  multiSelected: string[] = [];
  activeTemplate: string | null = null;
  selectedLabels: string[] = [];

  ngOnChanges(): void {
    if (this.field.uiHint === 'multiselect') {
      this.multiSelected = Array.isArray(this.value) ? (this.value as string[]) : [];
      this.selectedLabels = this.multiSelected.map(id =>
        this.field.allowedValues?.find(o => o.id === id)?.label ?? id);
    } else {
      this.strVal = this.value != null ? String(this.value) : null;
      this.numVal = this.value != null ? Number(this.value) : null;
    }
    this.selectOptions = (this.field.allowedValues ?? []).map(o => ({ value: o.id, label: o.label }));
    this.updateActiveTemplate();
  }

  onSelectChange(id: string | null): void {
    this.strVal = id;
    this.updateActiveTemplate();
    this.emit(id);
  }

  emit(val: unknown): void {
    const coerced = val == null || val === ''
      ? null
      : this.field.dataType === 'decimal' || this.field.dataType === 'int'
        ? Number(val)
        : val;
    this.valueChange.emit(coerced);
  }

  isSelected(id: string): boolean {
    return this.multiSelected.includes(id);
  }

  toggleMulti(id: string): void {
    const idx = this.multiSelected.indexOf(id);
    if (idx >= 0) {
      this.multiSelected = this.multiSelected.filter(v => v !== id);
    } else {
      this.multiSelected = [...this.multiSelected, id];
    }
    this.selectedLabels = this.multiSelected.map(v =>
      this.field.allowedValues?.find(o => o.id === v)?.label ?? v);
    this.valueChange.emit(this.multiSelected.length > 0 ? this.multiSelected : null);
  }

  private updateActiveTemplate(): void {
    if (this.strVal && this.field.allowedValues) {
      const opt = this.field.allowedValues.find(o => o.id === this.strVal);
      this.activeTemplate = opt?.template ?? null;
    } else {
      this.activeTemplate = null;
    }
  }
}
