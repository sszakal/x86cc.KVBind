import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnChanges, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AppSelectComponent } from '../../shared/components/app-select.component';
import { AppDatePickerComponent } from '../../shared/components/app-date-picker.component';
import { KVFieldSchema, annotation, constraintHints, controlOf, labelOf, optionsOf, UI_HELP } from './schema';

// One field, rendered from its schema: the control comes from the DSL's ui:control annotation or the
// field's kind, options from its allowed values or enum members, hints from its constraints and default.
// Emits the value to SET (numbers as numbers, lists as arrays, flags as "A, B"), or null to UNSET.
@Component({
  selector: 'app-schema-field',
  standalone: true,
  imports: [CommonModule, FormsModule, AppSelectComponent, AppDatePickerComponent],
  template: `
    <div class="block" [attr.data-path]="path">
      <div class="flex items-center gap-1.5 text-sm font-medium text-gray-700 dark:text-gray-300">
        <span>{{ label }}</span>
        @if (field.isRequired) { <span class="text-red-500" title="Required">*</span> }
        @if (changed) { <span class="h-1.5 w-1.5 rounded-full bg-orange-400" title="Changed in this draft"></span> }
        @if (inherited) {
          <span class="rounded bg-sky-100 px-1.5 py-0.5 text-[10px] font-medium text-sky-700 dark:bg-sky-950/40 dark:text-sky-300" title="Read from the parent aggregate">inherited</span>
        } @else if (readOnly) {
          <span class="rounded bg-gray-100 px-1.5 py-0.5 text-[10px] font-medium text-gray-500 dark:bg-gray-800 dark:text-gray-400" title="PatchReadOnly: set by the model, not by patches">read-only</span>
        }
      </div>

      @if (locked) {
        <p class="mt-1.5 min-h-11 rounded-lg border border-dashed border-gray-200 bg-gray-50 px-4 py-2.5 text-sm text-gray-700 dark:border-gray-700 dark:bg-gray-900/40 dark:text-gray-300">
          {{ displayValue }}
        </p>
      } @else {
        @switch (control) {
          @case ('textarea') {
            <textarea class="mt-1.5 min-h-[80px] w-full rounded-lg border border-gray-300 px-4 py-2.5 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
              [ngModel]="text" (ngModelChange)="emitText($event)" [attr.maxlength]="field.maxLength"></textarea>
          }
          @case ('select') {
            <div class="mt-1.5">
              <app-select [options]="selectOptions" [value]="text" [placeholder]="'Select ' + label.toLowerCase() + '…'" (valueChange)="emitText($event)"></app-select>
              @if (activeTemplate) {
                <p class="mt-1.5 rounded-lg bg-blue-50 px-3 py-2 text-xs text-blue-700 dark:bg-blue-950/30 dark:text-blue-300"><span class="font-medium">Template: </span>{{ activeTemplate }}</p>
              }
            </div>
          }
          @case ('radio') {
            <div class="mt-2 flex flex-wrap gap-2">
              @for (opt of options; track opt.id) {
                <button type="button" class="rounded-lg border px-4 py-2 text-sm font-medium transition-colors"
                  [ngClass]="text === opt.id ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300' : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800'"
                  (click)="emitText(opt.id)">{{ opt.label }}</button>
              }
              <button type="button" class="rounded-lg border border-dashed border-gray-200 px-3 py-2 text-xs text-gray-400 dark:border-gray-700" (click)="emitText(null)">Clear</button>
            </div>
          }
          @case ('multiselect') {
            <div class="mt-2 flex flex-wrap gap-2">
              @for (opt of options; track opt.id) {
                <button type="button" class="rounded-lg border px-3 py-2 text-sm transition-colors"
                  [ngClass]="selected.includes(opt.id) ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300' : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800'"
                  (click)="toggle(opt.id)">{{ selected.includes(opt.id) ? '✓ ' : '' }}{{ opt.label }}</button>
              }
              @for (unknown of unknownSelected; track unknown) {
                <button type="button" class="rounded-lg border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700 dark:border-red-900/60 dark:bg-red-950/30 dark:text-red-300"
                  title="Not an allowed value — kept for validation; click to remove" (click)="toggle(unknown)">✕ {{ unknown }}</button>
              }
            </div>
          }
          @case ('checkbox') {
            <div class="mt-2 flex gap-2">
              @for (choice of booleanChoices; track choice.label) {
                <button type="button" class="rounded-lg border px-4 py-2 text-sm font-medium"
                  [ngClass]="value === choice.value ? 'border-brand-500 bg-brand-50 text-brand-700 dark:bg-brand-950/30 dark:text-brand-300' : 'border-gray-200 text-gray-600 hover:bg-gray-50 dark:border-gray-700 dark:text-gray-300 dark:hover:bg-gray-800'"
                  (click)="valueChange.emit(choice.value)">{{ choice.label }}</button>
              }
            </div>
          }
          @case ('date') {
            <div class="mt-1.5">
              <app-date-picker [value]="text" [placeholder]="'Select ' + label.toLowerCase() + '…'" (valueChange)="emitText($event)"></app-date-picker>
            </div>
          }
          @case ('datetime') {
            <input type="datetime-local" class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm dark:border-gray-700 dark:bg-gray-900 dark:text-white"
              [ngModel]="localDateTime" (ngModelChange)="emitDateTime($event)" />
          }
          @case ('time') {
            <input type="time" step="1" class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm dark:border-gray-700 dark:bg-gray-900 dark:text-white"
              [ngModel]="text" (ngModelChange)="emitTime($event)" />
          }
          @case ('number') {
            <input type="number" class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
              [ngModel]="number" (ngModelChange)="emitNumber($event)" [step]="field.kind === 'Integer' ? 1 : 0.01" [attr.min]="field.min" [attr.max]="field.max" />
          }
          @case ('list') {
            <input type="text" class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm dark:border-gray-700 dark:bg-gray-900 dark:text-white"
              [ngModel]="listText" (ngModelChange)="emitList($event)" placeholder="Comma-separated values" />
          }
          @default {
            <input type="text" class="mt-1.5 h-11 w-full rounded-lg border border-gray-300 px-4 text-sm focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 dark:border-gray-700 dark:bg-gray-900 dark:text-white"
              [ngModel]="text" (ngModelChange)="emitText($event)" [attr.maxlength]="field.maxLength" [placeholder]="field.kind === 'Duration' ? 'hh:mm:ss' : label" />
          }
        }
      }

      @if (help || hints.length > 0) {
        <p class="mt-1 text-xs text-gray-400">{{ help }}@if (help && hints.length > 0) { · }{{ hints.join(' · ') }}</p>
      }
      @for (message of errors; track message) {
        <p class="mt-1 text-xs font-medium text-red-600 dark:text-red-400">{{ message }}</p>
      }
    </div>
  `,
})
export class SchemaFieldComponent implements OnChanges {
  @Input({ required: true }) field!: KVFieldSchema;
  @Input() path = '';
  @Input() value: unknown = null;
  @Input() readOnly = false;
  @Input() inherited = false;
  @Input() changed = false;
  @Input() errors: string[] = [];
  @Output() valueChange = new EventEmitter<unknown>();

  label = '';
  control = 'text';
  help: string | null = null;
  hints: string[] = [];
  options: Array<{ id: string; label: string; template: string | null }> = [];
  selectOptions: Array<{ value: string; label: string }> = [];
  text: string | null = null;
  number: number | null = null;
  listText = '';
  localDateTime = '';
  selected: string[] = [];
  unknownSelected: string[] = [];
  activeTemplate: string | null = null;
  displayValue = '—';

  readonly booleanChoices = [{ label: 'Yes', value: true }, { label: 'No', value: false }, { label: 'Not set', value: null }];

  get locked(): boolean { return this.readOnly || this.inherited; }

  ngOnChanges(): void {
    this.label = labelOf(this.field);
    this.control = controlOf(this.field);
    this.help = annotation(this.field.annotations, UI_HELP);
    this.hints = constraintHints(this.field);
    this.options = optionsOf(this.field);
    this.selectOptions = this.options.map(o => ({ value: o.id, label: o.label }));

    const value = this.value;
    this.text = value == null ? null : String(value);
    this.number = value == null || value === '' ? null : Number(value);
    this.selected = Array.isArray(value)
      ? value.map(v => String(v))
      : typeof value === 'string' && this.field.isFlags ? value.split(',').map(s => s.trim()).filter(s => s) : [];
    this.unknownSelected = this.selected.filter(id => !this.options.some(o => o.id === id));
    this.listText = Array.isArray(value) ? value.join(', ') : '';
    this.localDateTime = typeof value === 'string' && value ? toLocalInput(value) : '';
    this.activeTemplate = this.options.find(o => o.id === this.text)?.template ?? null;
    this.displayValue = this.describe(value);
  }

  emitText(value: string | null): void { this.valueChange.emit(value == null || value === '' ? null : value); }

  emitNumber(value: number | string | null): void {
    this.valueChange.emit(value == null || value === '' ? null : Number(value));
  }

  emitDateTime(value: string): void { this.valueChange.emit(value ? new Date(value).toISOString() : null); }

  emitTime(value: string): void { this.valueChange.emit(!value ? null : value.length === 5 ? `${value}:00` : value); }

  emitList(text: string): void {
    const items = text.split(',').map(s => s.trim()).filter(s => s.length > 0);
    this.valueChange.emit(items.length > 0 ? items : null);
  }

  toggle(id: string): void {
    const next = this.selected.includes(id) ? this.selected.filter(v => v !== id) : [...this.selected, id];
    if (next.length === 0) this.valueChange.emit(null);
    else this.valueChange.emit(this.field.isFlags && !this.field.isArray ? next.join(', ') : next);
  }

  private describe(value: unknown): string {
    if (value == null || value === '') return '—';
    const labelOfId = (id: string) => this.options.find(o => o.id === id)?.label ?? id;
    if (Array.isArray(value)) return value.map(v => labelOfId(String(v))).join(', ');
    if (typeof value === 'boolean') return value ? 'Yes' : 'No';
    return labelOfId(String(value));
  }
}

// An ISO timestamp as the value of a datetime-local input (local time, minutes precision).
function toLocalInput(iso: string): string {
  const date = new Date(iso);
  if (isNaN(date.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
