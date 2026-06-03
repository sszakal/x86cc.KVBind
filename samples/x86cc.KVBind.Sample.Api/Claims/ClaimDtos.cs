using System.Text.Json;

namespace x86cc.KVBind.Sample.Api.Claims;

public sealed record CreateClaimRequest(
    string ClaimNumber,
    string? IncidentDate,
    string? Description,
    string? PolicyNumber,
    string? CoverageType,
    string User);

public sealed record OpenClaimDraftRequest(string User);

public sealed record CommitClaimDraftRequest(string User);

public sealed record ClaimPatchOperationRequest(string OperationCode, string Path, JsonElement? Value = null);

public sealed record ClaimSummaryResponse(
    Guid ClaimId,
    string? ClaimNumber,
    string? Status,
    string? Description,
    decimal ClaimedTotal,
    Guid SnapshotVersion,
    Guid? LastCommitId);

public sealed record ClaimSnapshotResponse(
    Guid ClaimId,
    ClaimDataResponse Claim,
    Guid SnapshotVersion,
    Guid? LastCommitId,
    DateTimeOffset? LastCommitTimestamp);

public sealed record ClaimDraftResponse(
    Guid DraftId,
    Guid ClaimId,
    string User,
    ClaimDataResponse Claim,
    Guid BaseSnapshotVersion,
    Guid? BaseCommitId,
    IReadOnlyList<ClaimChangeResponse> Changes);

public sealed record ClaimCommitResponse(
    Guid ClaimId,
    Guid DraftId,
    Guid CommitId,
    ClaimSnapshotResponse Snapshot);

public sealed record CommitClaimDraftResult(ClaimCommitResponse? Commit, StaleDraftResponse? StaleDraft)
{
    public static CommitClaimDraftResult Committed(ClaimCommitResponse commit) => new(commit, null);

    public static CommitClaimDraftResult Stale(StaleDraftResponse staleDraft) => new(null, staleDraft);
}

public sealed record StaleDraftResponse(
    Guid ClaimId,
    Guid DraftId,
    Guid DraftBaseSnapshotVersion,
    Guid LatestSnapshotVersion,
    Guid? DraftBaseCommitId,
    Guid? LatestCommitId,
    string Message);

public sealed record ClaimChangeSetResponse(
    Guid CommitId,
    Guid? PreviousCommitId,
    string User,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> AddedOrChangedPaths,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<ClaimChangeResponse> Changes);

public sealed record ClaimDataResponse(
    string? ClaimNumber,
    string? Status,
    string? Priority,
    string? IncidentDate,
    string? Description,
    decimal ClaimedTotal,
    ClaimPolicyResponse Policy,
    IReadOnlyList<DamagedItemResponse> DamagedItems,
    IReadOnlyList<ClaimNoteResponse> Notes,
    ClaimantResponse? Claimant);

public sealed record ClaimPolicyResponse(string? PolicyNumber, string? CoverageType);

public sealed record DamagedItemResponse(string ItemId, string? Description, string? Category, decimal EstimatedAmount);

public sealed record ClaimSchemaResponse(
    IReadOnlyList<string> StatusValues,
    IReadOnlyList<string> PriorityValues,
    IReadOnlyList<string> CoverageTypeValues,
    IReadOnlyList<string> DamageCategories);

// Full definition schema — drives auto-generated form rendering on the frontend.
public sealed record DefinitionSchemaResponse(
    IReadOnlyList<FieldMeta> Fields,
    IReadOnlyList<FieldGroupMeta> FieldGroups,
    IReadOnlyList<CollectionMeta> Collections,
    IReadOnlyList<NestedNodeMeta> NestedNodes);

public sealed record FieldMeta(
    string Key,
    string Label,
    string DataType,           // string | decimal | int | bool | date
    string UiHint,             // text | textarea | select | radio | number | date
    bool IsRequired,
    IReadOnlyList<string>? AllowedValues);

public sealed record FieldGroupMeta(
    string Key,
    string Label,
    IReadOnlyList<FieldMeta> Fields);

public sealed record CollectionItemTypeMeta(
    string Token,
    string Label,
    IReadOnlyList<FieldMeta> Fields);

public sealed record CollectionMeta(
    string Key,
    string Label,
    IReadOnlyList<CollectionItemTypeMeta> ItemTypes);

public sealed record NestedNodeTypeMeta(
    string Token,
    string Label,
    IReadOnlyList<FieldMeta> Fields);

public sealed record NestedNodeMeta(
    string Key,
    string Label,
    IReadOnlyList<NestedNodeTypeMeta> Types);

public sealed record ClaimNoteResponse(string ItemId, string? Text);

public sealed record ClaimantResponse(string Type, string? DisplayName);

public sealed record ClaimChangeResponse(string Path, string ChangeType, object? OldValue = null, object? NewValue = null);
