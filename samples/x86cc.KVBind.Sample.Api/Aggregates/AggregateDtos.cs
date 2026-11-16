using System.Text.Json;
using System.Text.Json.Nodes;
using x86cc.KVBind.Sample.Api.Persistence;

namespace x86cc.KVBind.Sample.Api.Aggregates;

public sealed record ModelInfoResponse(
    string Name,
    string Title,
    string? Description,
    int SchemaVersion,
    IReadOnlyList<string> CreateFields,
    IReadOnlyList<string> SummaryFields,
    bool SupportsChildren,
    bool HasLegacySample);

/// <summary>Creates an aggregate. <c>Values</c> maps paths (e.g. <c>ClaimNumber</c>) to their first values.</summary>
public sealed record CreateAggregateRequest(
    string User,
    IReadOnlyDictionary<string, JsonElement>? Values = null,
    Guid? ParentId = null);

public sealed record OpenDraftRequest(string User);

public sealed record CommitDraftRequest(string User);

public sealed record PatchOperationRequest(string OperationCode, string Path, JsonElement? Value = null);

public sealed record AggregateSummaryResponse(
    Guid Id,
    Guid? ParentId,
    JsonObject Summary,
    Guid? LastCommitId,
    DateTimeOffset Modified,
    string ModifiedBy,
    int SchemaVersion);

public sealed record AggregateSnapshotResponse(
    Guid Id,
    string Model,
    Guid? ParentId,
    JsonObject Data,
    IReadOnlyDictionary<string, object?> Computed,
    IReadOnlyList<string> InheritedPaths,
    Guid? LastCommitId,
    DateTimeOffset? LastCommitTimestamp,
    int SchemaVersion);

public sealed record AggregateDraftResponse(
    Guid DraftId,
    Guid AggregateId,
    string Model,
    string User,
    Guid? ParentId,
    JsonObject Data,
    IReadOnlyDictionary<string, object?> Computed,
    IReadOnlyList<string> InheritedPaths,
    Guid? BaseCommitId,
    IReadOnlyList<AggregateChangeRow> Changes,
    IReadOnlyList<string> ChangedPaths,

    // Draft state — drives the editor vs. forced-merge screen on the frontend.
    bool IsRebasing,
    bool IsStale,
    Guid? LatestCommitId,
    Guid? RebaseTargetCommitId,
    IReadOnlyList<RebaseConflictResponse> Conflicts,
    IReadOnlyList<AggregateEventRecord> PendingEvents,

    // Draft changes lost when the draft was moved onto a migrated snapshot (see DraftMigrationNotice).
    IReadOnlyList<DraftMigrationNotice> MigrationNotices);

public sealed record RebaseConflictResponse(
    string Path,
    string DisplayPath,
    string Kind,             // Value | DeleteEdit | Structural | Incoming | IncomingItem | Order
    string Resolution,       // Unresolved | Ours | Theirs | Custom
    object? BaseValue,       // V1 — common ancestor
    object? MainValue,       // V2 — committed upstream
    object? OursValue,       // draft value (null when the draft deleted/never touched the path)
    bool IsIncoming,         // true = non-conflicting incoming change (default accept, rejectable)
    bool RequiresResolution, // true = real conflict that blocks finishing until resolved
    bool IsFixed);           // true = conflict with a fixed outcome (Order): shown, not resolvable

public sealed record RebaseResultResponse(
    Guid AggregateId,
    Guid DraftId,
    string Outcome,        // AlreadyCurrent | CanAutomerge | HasUnresolvedConflicts
    Guid? TargetCommitId,
    IReadOnlyList<RebaseConflictResponse> Conflicts);

public sealed record ResolveRebaseConflictRequest(
    string Path,
    string Resolution,     // Ours | Theirs | Custom
    JsonElement? Value = null);

public sealed record CommitResponse(
    Guid AggregateId,
    Guid DraftId,
    Guid CommitId,
    AggregateSnapshotResponse Snapshot,
    IReadOnlyList<AggregateEventRecord> Events);

public sealed record CommitResult(CommitResponse? Commit, StaleDraftResponse? StaleDraft, ValidateDraftResponse? Invalid)
{
    public static CommitResult Committed(CommitResponse commit) => new(commit, null, null);

    public static CommitResult Stale(StaleDraftResponse staleDraft) => new(null, staleDraft, null);

    public static CommitResult Rejected(ValidateDraftResponse validation) => new(null, null, validation);
}

public sealed record StaleDraftResponse(
    Guid AggregateId,
    Guid DraftId,
    Guid? DraftBaseCommitId,
    Guid? LatestCommitId,
    string Message);

public sealed record ChangeSetResponse(
    Guid CommitId,
    Guid? PreviousCommitId,
    string User,
    DateTimeOffset Timestamp,
    int? MigrationToVersion,
    IReadOnlyList<AggregateChangeRow> Changes,
    IReadOnlyList<AggregateEventRecord> Events);

/// <summary>One page of commits, newest first: <see cref="Total"/> commits in all, this page from <see cref="Offset"/>.</summary>
public sealed record ChangeSetPageResponse(
    IReadOnlyList<ChangeSetResponse> Items,
    int Total,
    int Offset,
    int Limit);

public sealed record ValidateDraftResponse(
    string Profile,
    bool IsValid,
    IReadOnlyList<ValidationErrorResponse> Errors);

public sealed record ValidationErrorResponse(string Path, string DisplayPath, string Code, string Message);

public sealed record MigrationBatchResponse(int Scanned, int Migrated, int Commits, int SchemaVersion);
