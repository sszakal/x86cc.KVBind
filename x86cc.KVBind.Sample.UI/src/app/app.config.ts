import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';

import { routes } from './app.routes';
import { timingInterceptor } from './core/perf/perf.service';
import { provideOperationRenderers } from './features/models/operations/operation-renderers';
import { SAMPLE_OPERATION_RENDERERS } from './features/models/operations/sample-operations';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideHttpClient(withInterceptors([timingInterceptor])),
    provideRouter(routes, withComponentInputBinding()),
    provideOperationRenderers(SAMPLE_OPERATION_RENDERERS),
  ],
};
