import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map, shareReplay } from 'rxjs';
import { DataObject, KVSchemaDocument, resolveReferences } from './schema';

// The generic API (AggregateEndpoints.cs): one set of routes for every model, /api/models/{model}/...

export interface ModelInfo {
  name: string;
  title: string;
  description: string | null;
  schemaVersion: number;
  createFields: string[];
  summaryFields: string[];
  supportsChildren: boolean;
  hasLegacySample: boolean;
}

export interface AggregateSummary {
  id: string;
  parentId: string | null;
  summary: DataObject;
  lastCommitId: string | null;
  modified: string;
  modifiedBy: string;
  schemaVersion: number;
}

export interface AggregateSnapshot {
  id: string;
  model: string;
  parentId: string | null;
  data: DataObject;
  computed: Record<string, unknown>;
  inheritedPaths: string[];
  lastCommitId: string | null;
  lastCommitTimestamp: string | null;
  schemaVersion: number;
}

export interface ChangeRow { path: string; changeType: string; oldValue: unknown; newValue: unknown; }

export interface DomainEvent { type: string; payload: string; raisedAt: string; }

export interface RebaseConflict {
  path: string;
  displayPath: string;
  kind: 'Value' | 'DeleteEdit' | 'Structural' | 'Incoming' | 'IncomingItem' | 'Order';
  resolution: 'Unresolved' | 'Ours' | 'Theirs' | 'Custom';
  baseValue: unknown;
  mainValue: unknown;
  oursValue: unknown;
  isIncoming: boolean;
  requiresResolution: boolean;
  // A conflict with a fixed outcome (Order): shown for information, it cannot be resolved.
  isFixed: boolean;
}

export interface AggregateDraft {
  draftId: string;
  aggregateId: string;
  model: string;
  user: string;
  parentId: string | null;
  data: DataObject;
  computed: Record<string, unknown>;
  inheritedPaths: string[];
  baseCommitId: string | null;
  changes: ChangeRow[];
  changedPaths: string[];
  isRebasing: boolean;
  isStale: boolean;
  latestCommitId: string | null;
  rebaseTargetCommitId: string | null;
  conflicts: RebaseConflict[];
  pendingEvents: DomainEvent[];
}

export interface PatchOperation { operationCode: string; path: string; value?: unknown; }

export interface ValidationError { path: string; displayPath: string; code: string; message: string; }

export interface ValidationResult { profile: string; isValid: boolean; errors: ValidationError[]; }

export interface RebaseResult {
  aggregateId: string;
  draftId: string;
  outcome: 'AlreadyCurrent' | 'CanAutomerge' | 'HasUnresolvedConflicts';
  targetCommitId: string | null;
  conflicts: RebaseConflict[];
}

export interface CommitResult { aggregateId: string; draftId: string; commitId: string; snapshot: AggregateSnapshot; events: DomainEvent[]; }

export interface ChangeSetPage { items: ChangeSet[]; total: number; offset: number; limit: number; }

export interface ChangeSet {
  commitId: string;
  previousCommitId: string | null;
  user: string;
  timestamp: string;
  migrationToVersion: number | null;
  changes: ChangeRow[];
  events: DomainEvent[];
}

export interface MigrationBatchResult { scanned: number; migrated: number; commits: number; schemaVersion: number; }

export const API_ROOT = 'http://localhost:5101/api/models';

@Injectable({ providedIn: 'root' })
export class ModelApiService {
  private readonly definitions = new Map<string, Observable<KVSchemaDocument>>();

  constructor(private readonly http: HttpClient) {}

  listModels(): Observable<ModelInfo[]> { return this.http.get<ModelInfo[]>(API_ROOT); }

  info(model: string): Observable<ModelInfo> { return this.http.get<ModelInfo>(`${API_ROOT}/${model}/info`); }

  // The schema never changes while the app runs, so it is fetched once per model.
  definition(model: string): Observable<KVSchemaDocument> {
    let cached = this.definitions.get(model);
    if (!cached) {
      cached = this.http.get<KVSchemaDocument>(`${API_ROOT}/${model}/definition`).pipe(map(resolveReferences), shareReplay(1));
      this.definitions.set(model, cached);
    }
    return cached;
  }

  list(model: string): Observable<AggregateSummary[]> { return this.http.get<AggregateSummary[]>(`${API_ROOT}/${model}`); }

  create(model: string, user: string, values: Record<string, unknown>, parentId?: string): Observable<AggregateSnapshot> {
    return this.http.post<AggregateSnapshot>(`${API_ROOT}/${model}`, { user, values, parentId: parentId ?? null });
  }

  // Stores an aggregate in the model's old layout, so its migrations can be watched running.
  createLegacy(model: string, user: string): Observable<AggregateSummary> {
    return this.http.post<AggregateSummary>(`${API_ROOT}/${model}/legacy`, { user });
  }

  migrateAll(model: string): Observable<MigrationBatchResult> {
    return this.http.post<MigrationBatchResult>(`${API_ROOT}/${model}/migrate`, {});
  }

  snapshot(model: string, id: string): Observable<AggregateSnapshot> {
    return this.http.get<AggregateSnapshot>(`${API_ROOT}/${model}/${id}/snapshot`);
  }

  // One page of the history, newest first.
  changeSets(model: string, id: string, offset = 0, limit = 50): Observable<ChangeSetPage> {
    return this.http.get<ChangeSetPage>(`${API_ROOT}/${model}/${id}/changesets`, { params: { offset, limit } });
  }

  openDraft(model: string, id: string, user: string): Observable<AggregateDraft> {
    return this.http.post<AggregateDraft>(`${API_ROOT}/${model}/${id}/drafts`, { user });
  }

  draft(model: string, id: string, draftId: string): Observable<AggregateDraft> {
    return this.http.get<AggregateDraft>(this.draftUrl(model, id, draftId));
  }

  patch(model: string, id: string, draftId: string, operations: PatchOperation[]): Observable<AggregateDraft> {
    return this.http.post<AggregateDraft>(`${this.draftUrl(model, id, draftId)}/patch`, operations);
  }

  validate(model: string, id: string, draftId: string): Observable<ValidationResult> {
    return this.http.post<ValidationResult>(`${this.draftUrl(model, id, draftId)}/validate`, {});
  }

  reset(model: string, id: string, draftId: string): Observable<AggregateDraft> {
    return this.http.post<AggregateDraft>(`${this.draftUrl(model, id, draftId)}/reset`, {});
  }

  commit(model: string, id: string, draftId: string, user: string): Observable<CommitResult> {
    return this.http.post<CommitResult>(`${this.draftUrl(model, id, draftId)}/commit`, { user });
  }

  beginRebase(model: string, id: string, draftId: string): Observable<RebaseResult> {
    return this.http.post<RebaseResult>(`${this.draftUrl(model, id, draftId)}/rebase`, {});
  }

  resolveConflict(model: string, id: string, draftId: string, path: string, resolution: 'Ours' | 'Theirs' | 'Custom', value?: unknown): Observable<RebaseResult> {
    return this.http.post<RebaseResult>(`${this.draftUrl(model, id, draftId)}/rebase/resolve`, { path, resolution, value });
  }

  finishRebase(model: string, id: string, draftId: string): Observable<AggregateDraft> {
    return this.http.post<AggregateDraft>(`${this.draftUrl(model, id, draftId)}/rebase/finish`, {});
  }

  cancelRebase(model: string, id: string, draftId: string): Observable<AggregateDraft> {
    return this.http.delete<AggregateDraft>(`${this.draftUrl(model, id, draftId)}/rebase`);
  }

  private draftUrl(model: string, id: string, draftId: string): string {
    return `${API_ROOT}/${model}/${id}/drafts/${draftId}`;
  }
}

// The { error } body the API returns for 400 / 409, else the HTTP message.
export function errorText(error: unknown): string {
  const e = error as { error?: { error?: string; title?: string; detail?: string }; message?: string };
  return e?.error?.error ?? e?.error?.detail ?? e?.error?.title ?? e?.message ?? 'Unknown error';
}
