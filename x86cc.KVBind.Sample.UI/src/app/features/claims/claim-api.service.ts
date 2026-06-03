import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface CreateClaimRequest {
  claimNumber: string;
  incidentDate: string | null;
  description: string | null;
  policyNumber: string | null;
  coverageType: string | null;
  user: string;
}

export interface OpenClaimDraftRequest {
  user: string;
}

export interface CommitClaimDraftRequest {
  user: string;
}

export interface ClaimSummaryResponse {
  claimId: string;
  claimNumber: string | null;
  status: string | null;
  description: string | null;
  claimedTotal: number;
  snapshotVersion: string;
  lastCommitId: string | null;
}

export interface ClaimSnapshotResponse {
  claimId: string;
  claim: ClaimDataResponse;
  snapshotVersion: string;
  lastCommitId: string | null;
  lastCommitTimestamp: string | null;
}

export interface ClaimDraftResponse {
  draftId: string;
  claimId: string;
  user: string;
  claim: ClaimDataResponse;
  baseSnapshotVersion: string;
  baseCommitId: string | null;
  changes: ClaimChangeResponse[];
}

export interface ClaimCommitResponse {
  claimId: string;
  draftId: string;
  commitId: string;
  snapshot: ClaimSnapshotResponse;
}

export interface StaleDraftResponse {
  claimId: string;
  draftId: string;
  draftBaseSnapshotVersion: string;
  latestSnapshotVersion: string;
  draftBaseCommitId: string | null;
  latestCommitId: string | null;
  message: string;
}

export interface ClaimChangeSetResponse {
  commitId: string;
  previousCommitId: string | null;
  user: string;
  timestamp: string;
  addedOrChangedPaths: string[];
  removedPaths: string[];
  changes: ClaimChangeResponse[];
}

export interface ClaimDataResponse {
  claimNumber: string | null;
  status: string | null;
  priority: string | null;
  incidentDate: string | null;
  description: string | null;
  claimedTotal: number;
  policy: ClaimPolicyResponse;
  damagedItems: DamagedItemResponse[];
  notes: ClaimNoteResponse[];
  claimant: ClaimantResponse | null;
}

export interface ClaimPolicyResponse {
  policyNumber: string | null;
  coverageType: string | null;
}

export interface DamagedItemResponse {
  itemId: string;
  description: string | null;
  category: string | null;
  estimatedAmount: number;
}

export interface ClaimSchemaResponse {
  statusValues: string[];
  priorityValues: string[];
  coverageTypeValues: string[];
  damageCategories: string[];
}

export interface ClaimNoteResponse {
  itemId: string;
  text: string | null;
}

export interface ClaimantResponse {
  type: string;
  displayName: string | null;
}

export interface ClaimChangeResponse {
  path: string;
  changeType: string;
  oldValue: unknown;
  newValue: unknown;
}

export interface ClaimPatchOperationRequest {
  operationCode: string;
  path: string;
  value?: unknown;
}

export interface FieldMeta { key: string; label: string; dataType: string; uiHint: string; isRequired: boolean; allowedValues: string[] | null; }
export interface FieldGroupMeta { key: string; label: string; fields: FieldMeta[]; }
export interface CollectionItemTypeMeta { token: string; label: string; fields: FieldMeta[]; }
export interface CollectionMeta { key: string; label: string; itemTypes: CollectionItemTypeMeta[]; }
export interface NestedNodeTypeMeta { token: string; label: string; fields: FieldMeta[]; }
export interface NestedNodeMeta { key: string; label: string; types: NestedNodeTypeMeta[]; }
export interface DefinitionSchemaResponse {
  fields: FieldMeta[];
  fieldGroups: FieldGroupMeta[];
  collections: CollectionMeta[];
  nestedNodes: NestedNodeMeta[];
}

@Injectable({ providedIn: 'root' })
export class ClaimApiService {
  readonly apiBaseUrl = 'http://localhost:5101/api/claims';

  constructor(private readonly http: HttpClient) {}

  getSchema(): Observable<ClaimSchemaResponse> {
    return this.http.get<ClaimSchemaResponse>(`${this.apiBaseUrl}/schema`);
  }

  getDefinition(): Observable<DefinitionSchemaResponse> {
    return this.http.get<DefinitionSchemaResponse>(`${this.apiBaseUrl}/definition`);
  }

  listClaims(): Observable<ClaimSummaryResponse[]> {
    return this.http.get<ClaimSummaryResponse[]>(this.apiBaseUrl);
  }

  createClaim(request: CreateClaimRequest): Observable<ClaimSnapshotResponse> {
    return this.http.post<ClaimSnapshotResponse>(this.apiBaseUrl, request);
  }

  getSnapshot(claimId: string): Observable<ClaimSnapshotResponse> {
    return this.http.get<ClaimSnapshotResponse>(`${this.apiBaseUrl}/${claimId}/snapshot`);
  }

  openDraft(claimId: string, request: OpenClaimDraftRequest): Observable<ClaimDraftResponse> {
    return this.http.post<ClaimDraftResponse>(`${this.apiBaseUrl}/${claimId}/drafts`, request);
  }

  getDraft(claimId: string, draftId: string): Observable<ClaimDraftResponse> {
    return this.http.get<ClaimDraftResponse>(`${this.apiBaseUrl}/${claimId}/drafts/${draftId}`);
  }

  patchDraft(claimId: string, draftId: string, operations: ClaimPatchOperationRequest[]): Observable<ClaimDraftResponse> {
    return this.http.post<ClaimDraftResponse>(`${this.apiBaseUrl}/${claimId}/drafts/${draftId}/patch`, operations);
  }

  commitDraft(claimId: string, draftId: string, request: CommitClaimDraftRequest): Observable<ClaimCommitResponse> {
    return this.http.post<ClaimCommitResponse>(`${this.apiBaseUrl}/${claimId}/drafts/${draftId}/commit`, request);
  }

  listChangeSets(claimId: string): Observable<ClaimChangeSetResponse[]> {
    return this.http.get<ClaimChangeSetResponse[]>(`${this.apiBaseUrl}/${claimId}/changesets`);
  }
}
