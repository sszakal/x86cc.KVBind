import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpRequest, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface TimingStage { name: string; ms: number; }

// One API call as the performance panel shows it: the server's stage timings (from the Server-Timing
// header written by the API's RequestTimings) next to the round trip the browser measured.
export interface RequestTiming {
  id: number;
  method: string;
  url: string;
  status: number;
  clientMs: number;
  serverMs: number | null;
  stages: TimingStage[];
  requestBytes: number;
  responseBytes: number;
  at: Date;
}

@Injectable({ providedIn: 'root' })
export class PerfService {
  private nextId = 1;
  readonly requests = signal<RequestTiming[]>([]);

  record(entry: Omit<RequestTiming, 'id'>): void {
    this.requests.update(list => [{ ...entry, id: this.nextId++ }, ...list].slice(0, 50));
  }

  clear(): void { this.requests.set([]); }
}

// "load;dur=1.2, bind;dur=0.3, total;dur=5" -> stages and the server total.
export function parseServerTiming(header: string | null): { stages: TimingStage[]; total: number | null } {
  const stages: TimingStage[] = [];
  let total: number | null = null;
  for (const part of (header ?? '').split(',')) {
    const [name, ...params] = part.trim().split(';');
    const duration = params.map(p => p.trim()).find(p => p.startsWith('dur='));
    if (!name || !duration) continue;
    const ms = Number(duration.slice(4));
    if (name === 'total') total = ms; else stages.push({ name, ms });
  }
  return { stages, total };
}

export function timingInterceptor(request: HttpRequest<unknown>, next: HttpHandlerFn): Observable<HttpEvent<unknown>> {
  const perf = inject(PerfService);
  const started = performance.now();
  const requestBytes = request.body == null ? 0 : JSON.stringify(request.body).length;
  const done = (status: number, header: string | null, body: unknown) => {
    const { stages, total } = parseServerTiming(header);
    perf.record({
      method: request.method,
      url: request.urlWithParams.replace(/^https?:\/\/[^/]+/, ''),
      status,
      clientMs: performance.now() - started,
      serverMs: total,
      stages,
      requestBytes,
      responseBytes: body == null ? 0 : JSON.stringify(body).length,
      at: new Date(),
    });
  };

  return next(request).pipe(
    tap({
      next: event => { if (event instanceof HttpResponse) done(event.status, event.headers.get('Server-Timing'), event.body); },
      error: (error: unknown) => { if (error instanceof HttpErrorResponse) done(error.status, error.headers?.get('Server-Timing') ?? null, error.error); },
    }),
  );
}
