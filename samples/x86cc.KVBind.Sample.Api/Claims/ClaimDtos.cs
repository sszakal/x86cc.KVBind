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

public sealed record ClaimChangeSetResponse(
    Guid CommitId,
    Guid? PreviousCommitId,
    string User,
    DateTimeOffset Timestamp,
    IReadOnlyList<string> AddedOrChangedPaths,
    IReadOnlyList<string> RemovedPaths);

public sealed record ClaimDataResponse(
    string? ClaimNumber,
    string? Status,
    string? IncidentDate,
    string? Description,
    decimal ClaimedTotal,
    ClaimPolicyResponse Policy,
    IReadOnlyList<DamagedItemResponse> DamagedItems,
    IReadOnlyList<ClaimNoteResponse> Notes,
    ClaimantResponse? Claimant);

public sealed record ClaimPolicyResponse(string? PolicyNumber, string? CoverageType);

public sealed record DamagedItemResponse(string ItemId, string? Description, decimal EstimatedAmount);

public sealed record ClaimNoteResponse(string ItemId, string? Text);

public sealed record ClaimantResponse(string Type, string? DisplayName);

public sealed record ClaimChangeResponse(string Path, string ChangeType);
