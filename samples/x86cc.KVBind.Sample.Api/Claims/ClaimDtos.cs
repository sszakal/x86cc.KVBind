using System.Text.Json;

namespace x86cc.KVBind.Sample.Api.Claims;

// Create only captures the mandatory identity of the claim. All other fields are filled in on the
// draft edit page after the claim (and its first draft) are opened.
public sealed record CreateClaimRequest(
    string ClaimNumber,
    string User);

public sealed record OpenClaimDraftRequest(string User);

public sealed record CommitClaimDraftRequest(string User);

public sealed record ClaimPatchOperationRequest(string OperationCode, string Path, JsonElement? Value = null);

public sealed record ClaimSummaryResponse(
    Guid ClaimId,
    string? ClaimNumber,
    string? Status,
    string? Priority,
    string? Description,
    decimal ClaimedTotal,
    Guid SnapshotVersion,
    Guid? LastCommitId,
    DateTimeOffset Modified);

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
    IReadOnlyList<ClaimChangeResponse> Changes,
    // Draft state — drives the editor vs. forced-merge screen on the frontend.
    bool IsRebasing,
    bool IsStale,
    Guid LatestSnapshotVersion,
    Guid? RebaseTargetVersion,
    IReadOnlyList<RebaseConflictResponse> Conflicts);

public sealed record RebaseConflictResponse(
    string Path,
    string Kind,             // Value | DeleteEdit | Structural | Incoming | IncomingItem
    string Resolution,       // Unresolved | Ours | Theirs | Custom
    object? BaseValue,       // V1 — common ancestor
    object? MainValue,       // V2 — committed upstream
    object? OursValue,       // draft value (null when the draft deleted/never touched the path)
    bool IsIncoming,         // true = non-conflicting incoming change (default accept, rejectable)
    bool RequiresResolution);// true = real conflict that blocks finishing until resolved

public sealed record RebaseResultResponse(
    Guid ClaimId,
    Guid DraftId,
    string Outcome,        // AlreadyCurrent | CanAutomerge | HasUnresolvedConflicts
    Guid TargetSnapshotVersion,
    IReadOnlyList<RebaseConflictResponse> Conflicts);

public sealed record ResolveRebaseConflictRequest(
    string Path,
    string Resolution,     // Ours | Theirs | Custom
    JsonElement? Value = null);

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
    string[]? Tags,
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

// Richer allowed value — carries label and optional template/placeholders
// for AllowedValueComponent (structured option with parameter inputs).
public sealed record AllowedValueOption(
    string Id,
    string Label,
    string? Template,
    IReadOnlyList<PlaceholderMeta>? Placeholders);

public sealed record PlaceholderMeta(
    string Name,
    string Label,
    string DataType);

public sealed record FieldMeta(
    string Key,
    string Label,
    string DataType,           // string | decimal | int | bool | date
    string UiHint,             // text | textarea | select | radio | number | date | multiselect
    bool IsRequired,
    IReadOnlyList<AllowedValueOption>? AllowedValues);

public sealed record ValidateDraftResponse(
    string Profile,
    bool IsValid,
    IReadOnlyList<ClaimValidationError> Errors);

public sealed record ClaimValidationError(string Path, string Code, string Message);

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
