import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, HostListener, Input, OnChanges, Output } from '@angular/core';

@Component({
  selector: 'app-select',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="relative">
      <button
        type="button"
        class="flex h-11 w-full items-center justify-between rounded-lg border px-4 text-sm transition-colors"
        [ngClass]="isOpen
          ? 'border-brand-500 ring-2 ring-brand-500/20 bg-white dark:bg-gray-900'
          : 'border-gray-300 bg-white hover:border-gray-400 dark:border-gray-700 dark:bg-gray-900'"
        (click)="toggle()">
        <span [ngClass]="selectedLabel ? 'text-gray-900 dark:text-white' : 'text-gray-400'">
          {{ selectedLabel || placeholder }}
        </span>
        <svg class="h-4 w-4 text-gray-400 transition-transform" [ngClass]="isOpen ? 'rotate-180' : ''" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 9l-7 7-7-7"/>
        </svg>
      </button>

      @if (isOpen) {
        <div class="absolute z-50 mt-1 w-full overflow-hidden rounded-xl border border-gray-200 bg-white shadow-lg dark:border-gray-700 dark:bg-gray-900">
          @if (clearable) {
            <button type="button"
              class="flex w-full items-center px-4 py-2.5 text-sm text-gray-400 hover:bg-gray-50 dark:hover:bg-gray-800"
              (click)="select(null)">
              — clear —
            </button>
          }
          @for (opt of options; track opt.value) {
            <button type="button"
              class="flex w-full items-center gap-2 px-4 py-2.5 text-sm transition-colors"
              [ngClass]="opt.value === value
                ? 'bg-brand-50 text-brand-700 font-semibold dark:bg-brand-950/30 dark:text-brand-300'
                : 'text-gray-700 hover:bg-gray-50 dark:text-gray-200 dark:hover:bg-gray-800'"
              (click)="select(opt.value)">
              @if (opt.value === value) {
                <svg class="h-3.5 w-3.5 shrink-0 text-brand-500" fill="currentColor" viewBox="0 0 20 20"><path fill-rule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clip-rule="evenodd"/></svg>
              } @else {
                <span class="h-3.5 w-3.5 shrink-0"></span>
              }
              {{ opt.label }}
            </button>
          }
        </div>
      }
    </div>
  `,
})
export class AppSelectComponent implements OnChanges {
  @Input() options: { value: string; label: string }[] = [];
  @Input() value: string | null = null;
  @Input() placeholder = 'Select…';
  @Input() clearable = true;
  @Output() valueChange = new EventEmitter<string | null>();

  isOpen = false;
  selectedLabel: string | null = null;

  constructor(private readonly el: ElementRef) {}

  ngOnChanges(): void {
    this.selectedLabel = this.options.find(o => o.value === this.value)?.label ?? null;
  }

  toggle(): void { this.isOpen = !this.isOpen; }

  select(value: string | null): void {
    this.value = value;
    this.selectedLabel = this.options.find(o => o.value === value)?.label ?? null;
    this.valueChange.emit(value);
    this.isOpen = false;
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.el.nativeElement.contains(event.target)) {
      this.isOpen = false;
    }
  }
}
