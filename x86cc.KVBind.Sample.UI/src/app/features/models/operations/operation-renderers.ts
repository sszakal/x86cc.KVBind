import { EnvironmentProviders, Injectable, InjectionToken, Type, inject, makeEnvironmentProviders } from '@angular/core';

// Custom operations (`Operation(...)` in the DSL) come from the schema by name only: their payload is a
// method argument the schema does not describe. So the UI renders an operation only when a renderer is
// registered for it; an operation without one is not shown at all.
//
// A renderer is a component with a `context` input. It shows whatever the operation needs (a button, a
// reason box, a small form) and calls `context.run(payload)` to send the operation as a patch.
//
//   provideOperationRenderers([
//     { operation: 'SUBMIT', component: ButtonOperationComponent },
//     { operation: 'QUOTE', model: 'underwriting', component: QuoteOperationComponent },
//   ])

/** What a renderer gets: the operation, where it applies, the current data there, and how to run it. */
export interface OperationContext {
  /** The model's route name, e.g. "underwriting". */
  readonly model: string;
  /** The operation name, e.g. "QUOTE". */
  readonly operation: string;
  /** Canonical path of the node or collection the operation targets ("" is the root). */
  readonly path: string;
  /** The draft's current data at `path`. */
  readonly data: unknown;
  /** True while a patch is in flight; renderers should disable their controls. */
  readonly busy: boolean;
  /** The `options` of the registration that matched. */
  readonly options: Readonly<Record<string, unknown>>;
  /** Sends the operation, with `payload` as the patch value (omit it for an operation without one). */
  run(payload?: unknown): void;
}

/** Interface a renderer component implements (it receives the context as an input). */
export interface OperationRendererComponent {
  context: OperationContext;
}

export interface OperationRenderer {
  /** The operation name as the schema lists it. */
  operation: string;
  /** Limit the renderer to one model; without it, it applies to the operation in every model. */
  model?: string;
  component: Type<OperationRendererComponent>;
  /** Free-form settings passed to the component as `context.options` (a label, a placeholder…). */
  options?: Record<string, unknown>;
}

export const OPERATION_RENDERERS = new InjectionToken<OperationRenderer[][]>('OPERATION_RENDERERS');

/** Registers operation renderers. Can be called more than once; later registrations win. */
export function provideOperationRenderers(renderers: OperationRenderer[]): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: OPERATION_RENDERERS, useValue: renderers, multi: true }]);
}

@Injectable({ providedIn: 'root' })
export class OperationRendererRegistry {
  private readonly renderers = (inject(OPERATION_RENDERERS, { optional: true }) ?? []).flat();

  /** The renderer for an operation in a model: a model-specific one first, else a model-wide one, else null. */
  find(model: string, operation: string): OperationRenderer | null {
    let fallback: OperationRenderer | null = null;
    for (let i = this.renderers.length - 1; i >= 0; i--) {
      const r = this.renderers[i];
      if (r.operation !== operation) continue;
      if (r.model === model) return r;
      if (r.model == null) fallback ??= r;
    }
    return fallback;
  }
}
