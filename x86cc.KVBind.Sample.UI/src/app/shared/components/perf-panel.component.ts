import { CommonModule } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { PerfService, RequestTiming } from '../../core/perf/perf.service';

// Floating panel with the latest API calls: the browser's round trip next to the server's stage timings
// (load, bind, patch, validate, project, persist, ...) from the Server-Timing header.
@Component({
  selector: 'app-perf-panel',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed bottom-16 right-4 z-50 flex flex-col items-end gap-2">
      @if (open()) {
        <div class="max-h-[60vh] w-[440px] max-w-[calc(100vw-2rem)] overflow-y-auto rounded-2xl border border-gray-200 bg-white p-4 shadow-2xl dark:border-gray-700 dark:bg-gray-900">
          <div class="mb-3 flex items-center justify-between">
            <p class="text-sm font-semibold text-gray-900 dark:text-white">Request timings</p>
            <button class="text-xs text-gray-400 hover:text-gray-700" (click)="perf.clear()">Clear</button>
          </div>
          @for (request of perf.requests(); track request.id) {
            <div class="mb-3 border-b border-gray-100 pb-3 last:border-0 dark:border-gray-800">
              <div class="flex items-center justify-between gap-2 text-xs">
                <span class="truncate font-mono text-gray-700 dark:text-gray-300" [title]="request.url">
                  <span class="font-semibold" [ngClass]="request.status >= 400 ? 'text-red-500' : 'text-emerald-600'">{{ request.method }} {{ request.status }}</span>
                  {{ shortUrl(request.url) }}
                </span>
                <span class="shrink-0 tabular-nums text-gray-500">{{ request.clientMs | number: '1.0-1' }} ms</span>
              </div>
              @if (request.stages.length > 0) {
                <div class="mt-1.5 flex h-2 overflow-hidden rounded bg-gray-100 dark:bg-gray-800" [title]="'server ' + (request.serverMs ?? 0) + ' ms'">
                  @for (stage of request.stages; track stage.name; let i = $index) {
                    <div [style.width.%]="percent(request, stage.ms)" [ngClass]="colors[i % colors.length]"></div>
                  }
                </div>
                <div class="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-[11px] text-gray-500">
                  @for (stage of request.stages; track stage.name; let i = $index) {
                    <span><span class="mr-1 inline-block h-2 w-2 rounded-sm" [ngClass]="colors[i % colors.length]"></span>{{ stage.name }} {{ stage.ms | number: '1.0-2' }}</span>
                  }
                  <span>· server {{ request.serverMs | number: '1.0-1' }} ms · {{ kb(request.responseBytes) }}</span>
                </div>
              }
            </div>
          } @empty { <p class="text-xs text-gray-400">No requests yet.</p> }
        </div>
      }
      <button class="rounded-full border border-gray-200 bg-white px-4 py-2 text-xs font-semibold text-gray-700 shadow-lg hover:bg-gray-50 dark:border-gray-700 dark:bg-gray-900 dark:text-gray-200"
        (click)="open.set(!open())">
        ⏱ {{ last() ? (last()!.clientMs | number: '1.0-0') + ' ms' : 'Timings' }}
      </button>
    </div>
  `,
})
export class PerfPanelComponent {
  readonly perf = inject(PerfService);
  readonly open = signal(false);
  readonly last = computed(() => this.perf.requests()[0] ?? null);
  readonly colors = ['bg-sky-400', 'bg-violet-400', 'bg-amber-400', 'bg-emerald-400', 'bg-rose-400', 'bg-indigo-400', 'bg-lime-400', 'bg-orange-400'];

  percent(request: RequestTiming, ms: number): number {
    const total = request.serverMs ?? request.stages.reduce((sum, s) => sum + s.ms, 0);
    return total > 0 ? Math.max(1, (ms / total) * 100) : 0;
  }

  shortUrl(url: string): string { return url.replace('/api/models/', '').replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, m => m.slice(0, 4) + '…'); }

  kb(bytes: number): string { return bytes > 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${bytes} B`; }
}
