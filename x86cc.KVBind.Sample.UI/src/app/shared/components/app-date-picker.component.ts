import { CommonModule } from '@angular/common';
import { Component, ElementRef, EventEmitter, HostListener, Input, OnChanges, Output } from '@angular/core';

interface DayCell {
  day: number;
  iso: string;        // yyyy-MM-dd
  inMonth: boolean;
  isToday: boolean;
  isSelected: boolean;
}

@Component({
  selector: 'app-date-picker',
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
        <span [ngClass]="value ? 'text-gray-900 dark:text-white' : 'text-gray-400'">
          {{ value ? displayLabel : placeholder }}
        </span>
        <svg class="h-4 w-4 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"/>
        </svg>
      </button>

      @if (isOpen) {
        <div class="absolute z-50 mt-1 w-72 rounded-xl border border-gray-200 bg-white p-3 shadow-lg dark:border-gray-700 dark:bg-gray-900">
          <!-- Month / year header -->
          <div class="mb-2 flex items-center justify-between">
            <button type="button" class="flex h-8 w-8 items-center justify-center rounded-lg text-gray-500 hover:bg-gray-100 dark:hover:bg-gray-800" (click)="shiftMonth(-1)">
              <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 19l-7-7 7-7"/></svg>
            </button>
            <span class="text-sm font-semibold text-gray-900 dark:text-white">{{ monthLabel }}</span>
            <button type="button" class="flex h-8 w-8 items-center justify-center rounded-lg text-gray-500 hover:bg-gray-100 dark:hover:bg-gray-800" (click)="shiftMonth(1)">
              <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5l7 7-7 7"/></svg>
            </button>
          </div>

          <!-- Weekday labels -->
          <div class="mb-1 grid grid-cols-7 gap-0.5">
            @for (wd of weekdays; track wd) {
              <span class="flex h-7 items-center justify-center text-[11px] font-medium uppercase text-gray-400">{{ wd }}</span>
            }
          </div>

          <!-- Day grid -->
          <div class="grid grid-cols-7 gap-0.5">
            @for (cell of cells; track cell.iso) {
              <button type="button"
                class="flex h-8 w-8 items-center justify-center rounded-lg text-sm transition-colors"
                [ngClass]="cell.isSelected
                  ? 'bg-brand-500 font-semibold text-white'
                  : cell.inMonth
                    ? (cell.isToday
                        ? 'font-semibold text-brand-600 ring-1 ring-brand-300 hover:bg-brand-50 dark:text-brand-300 dark:ring-brand-700 dark:hover:bg-brand-950/30'
                        : 'text-gray-700 hover:bg-gray-100 dark:text-gray-200 dark:hover:bg-gray-800')
                    : 'text-gray-300 hover:bg-gray-50 dark:text-gray-600 dark:hover:bg-gray-800/50'"
                (click)="select(cell.iso)">
                {{ cell.day }}
              </button>
            }
          </div>

          <!-- Footer -->
          <div class="mt-2 flex items-center justify-between border-t border-gray-100 pt-2 dark:border-gray-800">
            <button type="button" class="rounded-lg px-2 py-1 text-xs font-medium text-gray-400 hover:text-gray-600 dark:hover:text-gray-300" (click)="select(null)">Clear</button>
            <button type="button" class="rounded-lg px-2 py-1 text-xs font-medium text-brand-500 hover:text-brand-600" (click)="selectToday()">Today</button>
          </div>
        </div>
      }
    </div>
  `,
})
export class AppDatePickerComponent implements OnChanges {
  @Input() value: string | null = null;   // yyyy-MM-dd
  @Input() placeholder = 'Select date…';
  @Output() valueChange = new EventEmitter<string | null>();

  isOpen = false;
  displayLabel = '';

  // The month currently shown in the popover (1st of month, local time).
  private viewDate = new Date();

  private static readonly months = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December',
  ];
  weekdays = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];
  cells: DayCell[] = [];

  constructor(private readonly el: ElementRef) {}

  ngOnChanges(): void {
    const parsed = this.parse(this.value);
    this.displayLabel = parsed
      ? `${AppDatePickerComponent.months[parsed.getMonth()].slice(0, 3)} ${parsed.getDate()}, ${parsed.getFullYear()}`
      : '';
    this.viewDate = parsed ? new Date(parsed.getFullYear(), parsed.getMonth(), 1) : this.firstOfThisMonth();
    this.buildCells();
  }

  get monthLabel(): string {
    return `${AppDatePickerComponent.months[this.viewDate.getMonth()]} ${this.viewDate.getFullYear()}`;
  }

  toggle(): void {
    this.isOpen = !this.isOpen;
    if (this.isOpen) {
      const parsed = this.parse(this.value);
      this.viewDate = parsed ? new Date(parsed.getFullYear(), parsed.getMonth(), 1) : this.firstOfThisMonth();
      this.buildCells();
    }
  }

  shiftMonth(delta: number): void {
    this.viewDate = new Date(this.viewDate.getFullYear(), this.viewDate.getMonth() + delta, 1);
    this.buildCells();
  }

  select(iso: string | null): void {
    this.value = iso;
    this.valueChange.emit(iso);
    this.isOpen = false;
  }

  selectToday(): void {
    this.select(this.toIso(new Date()));
  }

  private buildCells(): void {
    const year = this.viewDate.getFullYear();
    const month = this.viewDate.getMonth();
    const firstWeekday = new Date(year, month, 1).getDay();
    const start = new Date(year, month, 1 - firstWeekday); // back up to the Sunday of the first week
    const todayIso = this.toIso(new Date());

    const cells: DayCell[] = [];
    for (let i = 0; i < 42; i++) {
      const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
      const iso = this.toIso(d);
      cells.push({
        day: d.getDate(),
        iso,
        inMonth: d.getMonth() === month,
        isToday: iso === todayIso,
        isSelected: iso === this.value,
      });
    }
    this.cells = cells;
  }

  // Parses yyyy-MM-dd as a local date (no timezone shift). Tolerates full ISO strings.
  private parse(value: string | null): Date | null {
    if (!value) return null;
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
    if (!match) return null;
    return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
  }

  private toIso(d: Date): string {
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${d.getFullYear()}-${m}-${day}`;
  }

  private firstOfThisMonth(): Date {
    const now = new Date();
    return new Date(now.getFullYear(), now.getMonth(), 1);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.el.nativeElement.contains(event.target)) {
      this.isOpen = false;
    }
  }
}
