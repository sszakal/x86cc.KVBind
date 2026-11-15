import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { RouterModule } from '@angular/router';
import { ModelApiService, ModelInfo, errorText } from './model-api.service';

// The catalog: every model the API serves. Each opens the same generic list / detail / editor pages.
@Component({
  selector: 'app-models-home',
  standalone: true,
  imports: [CommonModule, RouterModule],
  template: `
    <div class="space-y-6">
      <div class="rounded-2xl border border-gray-200 bg-white p-6 dark:border-gray-800 dark:bg-white/[0.03]">
        <p class="text-sm font-medium text-brand-500">KVBind sample</p>
        <h1 class="text-2xl font-semibold text-gray-900 dark:text-white">Models</h1>
        <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
          Every screen below is generated from the model's KVBind definition (served by <code>/api/models/&#123;model&#125;/definition</code>).
          Request timings from the API's <code>Server-Timing</code> header are in the performance panel (bottom right).
        </p>
      </div>
      @if (error) { <div class="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-700">{{ error }}</div> }
      <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        @for (model of models; track model.name) {
          <a [routerLink]="['/m', model.name]" class="rounded-2xl border border-gray-200 bg-white p-5 transition-colors hover:border-brand-300 dark:border-gray-800 dark:bg-white/[0.03]">
            <div class="flex items-center justify-between">
              <h2 class="text-base font-semibold text-gray-900 dark:text-white">{{ model.title }}</h2>
              <span class="rounded bg-gray-100 px-1.5 py-0.5 font-mono text-[11px] text-gray-500 dark:bg-gray-800">{{ model.name }}</span>
            </div>
            <p class="mt-2 text-sm text-gray-500">{{ model.description }}</p>
            <p class="mt-3 text-xs text-gray-400">schema v{{ model.schemaVersion }}@if (model.supportsChildren) { · parent/child }</p>
          </a>
        }
      </div>
    </div>
  `,
})
export class ModelsHomeComponent implements OnInit {
  models: ModelInfo[] = [];
  error = '';

  constructor(private readonly api: ModelApiService) {}

  ngOnInit(): void {
    this.api.listModels().subscribe({ next: m => (this.models = m), error: e => (this.error = errorText(e)) });
  }
}
