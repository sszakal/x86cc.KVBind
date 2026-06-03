using Marten;
using System.Text.Json;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api.Persistence;

namespace x86cc.KVBind.Sample.Api.Claims;

public sealed class InsuranceClaimAggregateService(
    IDocumentSession session,
    InsuranceClaimDefinitionFactory definitionFactory)
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new(JsonSerializerDefaults.Web);
    private const string StructureValue = "{...}";

    public async Task<ClaimSnapshotResponse> CreateClaimAsync(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClaimNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.User);

        var snapshot = new KVSnapshot
        {
            CreatedBy = request.User,
            ModifiedBy = request.User
        };
        var overlay = KVOverlay.Create(snapshot.Clone(), request.User);
        var root = Bind(overlay);

        root.ClaimNumber = request.ClaimNumber;
        root.Status = "Draft";
        root.IncidentDate = request.IncidentDate;
        root.Description = request.Description;
        root.Policy.PolicyNumber = request.PolicyNumber;
        root.Policy.CoverageType = request.CoverageType;

        var commit = root.CreateCommit(DateTimeOffset.UtcNow);
        var changes = ProjectCommitChanges(snapshot.Clone(), commit);
        snapshot.Apply(commit);

        session.Store(new ClaimSnapshotDocument
        {
            Id = snapshot.AggregateId,
            Snapshot = snapshot
        });
        session.Store(new ClaimChangeSetDocument
        {
            Id = commit.CommitId,
            ClaimId = snapshot.AggregateId,
            Commit = commit,
            Changes = changes
        });
        await session.SaveChangesAsync(cancellationToken);

        return ProjectSnapshot(snapshot);
    }

    public async Task<IReadOnlyList<ClaimSummaryResponse>> ListClaimsAsync(CancellationToken cancellationToken)
    {
        var documents = await session.Query<ClaimSnapshotDocument>().ToListAsync(cancellationToken);
        return documents
            .OrderBy(document => document.Snapshot.Modified)
            .Select(document =>
            {
                NormalizeSnapshot(document.Snapshot);
                var root = Bind(KVOverlay.Create(document.Snapshot.Clone(), "system"));
                return new ClaimSummaryResponse(
                    document.Id,
                    root.ClaimNumber,
                    root.Status,
                    root.Description,
                    root.ClaimedTotal,
                    document.Snapshot.Version,
                    document.Snapshot.LastCommitId);
            })
            .ToArray();
    }

    public async Task<ClaimSnapshotResponse?> GetSnapshotAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var document = await session.LoadAsync<ClaimSnapshotDocument>(claimId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        NormalizeSnapshot(document.Snapshot);
        return ProjectSnapshot(document.Snapshot);
    }

    public async Task<ClaimDraftResponse?> OpenDraftAsync(Guid claimId, OpenClaimDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.User);

        var snapshotDocument = await session.LoadAsync<ClaimSnapshotDocument>(claimId, cancellationToken);
        if (snapshotDocument is null)
        {
            return null;
        }

        NormalizeSnapshot(snapshotDocument.Snapshot);
        var overlay = KVOverlay.Create(snapshotDocument.Snapshot.Clone(), request.User);
        var draft = ClaimOverlayDocument.Create(claimId, request.User, overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        return ProjectDraft(draft);
    }

    public async Task<ClaimDraftResponse?> GetDraftAsync(Guid claimId, Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await session.LoadAsync<ClaimOverlayDocument>(draftId, cancellationToken);
        return draft is null || draft.ClaimId != claimId ? null : ProjectDraft(draft);
    }

    public async Task<ClaimDraftResponse?> PatchDraftAsync(
        Guid claimId,
        Guid draftId,
        IReadOnlyList<ClaimPatchOperationRequest> request,
        CancellationToken cancellationToken)
    {
        var draft = await session.LoadAsync<ClaimOverlayDocument>(draftId, cancellationToken);
        if (draft is null || draft.ClaimId != claimId)
        {
            return null;
        }

        var overlay = draft.ToOverlay();
        NormalizeOverlay(overlay);
        var root = Bind(overlay);
        root.Patch(request.Select(ToPatchOperation));

        draft.UpdateFrom(overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        return ProjectDraft(draft);
    }

    public async Task<CommitClaimDraftResult?> CommitDraftAsync(
        Guid claimId,
        Guid draftId,
        CommitClaimDraftRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await session.LoadAsync<ClaimOverlayDocument>(draftId, cancellationToken);
        if (draft is null || draft.ClaimId != claimId)
        {
            return null;
        }

        var snapshotDocument = await session.LoadAsync<ClaimSnapshotDocument>(claimId, cancellationToken);
        if (snapshotDocument is null)
        {
            return null;
        }

        NormalizeSnapshot(snapshotDocument.Snapshot);
        if (!IsDraftBasedOnLatestSnapshot(draft, snapshotDocument.Snapshot))
        {
            return CommitClaimDraftResult.Stale(new StaleDraftResponse(
                claimId,
                draftId,
                draft.BaseSnapshotVersion,
                snapshotDocument.Snapshot.Version,
                draft.BaseCommitId,
                snapshotDocument.Snapshot.LastCommitId,
                "Draft is based on an older snapshot. Sync with master before committing."));
        }

        var overlay = draft.ToOverlay();
        NormalizeOverlay(overlay);
        var root = Bind(overlay);
        var commit = root.CreateCommit(DateTimeOffset.UtcNow);
        var changes = ProjectCommitChanges(snapshotDocument.Snapshot.Clone(), commit);

        snapshotDocument.Snapshot.Apply(commit);
        if (!string.IsNullOrWhiteSpace(request.User))
        {
            snapshotDocument.Snapshot.ModifiedBy = request.User;
            commit.User = request.User;
        }

        session.Store(snapshotDocument);
        session.Store(new ClaimChangeSetDocument
        {
            Id = commit.CommitId,
            ClaimId = claimId,
            Commit = commit,
            Changes = changes
        });
        session.Delete<ClaimOverlayDocument>(draftId);
        await session.SaveChangesAsync(cancellationToken);

        return CommitClaimDraftResult.Committed(new ClaimCommitResponse(
            claimId,
            draftId,
            commit.CommitId,
            ProjectSnapshot(snapshotDocument.Snapshot)));
    }

    public async Task<IReadOnlyList<ClaimChangeSetResponse>> ListChangeSetsAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var documents = await session.Query<ClaimChangeSetDocument>()
            .Where(document => document.ClaimId == claimId)
            .ToListAsync(cancellationToken);

        return documents
            .OrderByDescending(document => document.Commit.Timestamp)
            .Select(document => new ClaimChangeSetResponse(
                document.Commit.CommitId,
                document.Commit.PreviousCommitId,
                document.Commit.User,
                document.Commit.Timestamp,
                document.Commit.AddedOrChanged.Keys.Order(StringComparer.Ordinal).ToArray(),
                document.Commit.Removed.Order(StringComparer.Ordinal).ToArray(),
                document.Changes.Count > 0
                    ? document.Changes
                    : ProjectCommitChanges(null, document.Commit)))
            .ToArray();
    }

    private InsuranceClaim Bind(KVOverlay overlay)
    {
        return KVRootNode.Create<InsuranceClaim>(overlay, definitionFactory.Definition);
    }

    private static bool IsDraftBasedOnLatestSnapshot(ClaimOverlayDocument draft, KVSnapshot latestSnapshot)
    {
        return draft.BaseSnapshotVersion == latestSnapshot.Version
               && draft.BaseCommitId == latestSnapshot.LastCommitId;
    }

    private ClaimSnapshotResponse ProjectSnapshot(KVSnapshot snapshot)
    {
        NormalizeSnapshot(snapshot);
        var root = Bind(KVOverlay.Create(snapshot.Clone(), "system"));
        return new ClaimSnapshotResponse(
            snapshot.AggregateId,
            ProjectClaim(root),
            snapshot.Version,
            snapshot.LastCommitId,
            snapshot.LastCommitTimestamp);
    }

    private ClaimDraftResponse ProjectDraft(ClaimOverlayDocument draft)
    {
        var overlay = draft.ToOverlay();
        NormalizeOverlay(overlay);
        var root = Bind(overlay);
        return new ClaimDraftResponse(
            draft.Id,
            draft.ClaimId,
            draft.User,
            ProjectClaim(root),
            overlay.BaseSnapshotVersion,
            overlay.BaseCommitId,
            ProjectOverlayChanges(overlay, root.GetAllChanges().Changes));
    }

    private static IReadOnlyList<ClaimChangeResponse> ProjectOverlayChanges(KVOverlay overlay, IEnumerable<KVChangeDelta> changes)
    {
        return changes
            .Select(change => new ClaimChangeResponse(
                NormalizeDisplayPath(change.Path),
                change.ChangeType.ToString(),
                ProjectPathValue(overlay.Snapshot, change.Path),
                ProjectPathValue(overlay, change.Path)))
            .GroupBy(change => change.Path, StringComparer.Ordinal)
            .Select(group => MergeChanges(group))
            .OrderBy(change => change.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ClaimChangeResponse> ProjectCommitChanges(KVSnapshot? beforeSnapshot, KVCommit commit)
    {
        var changes = new List<ClaimChangeResponse>();
        foreach (var pair in commit.AddedOrChanged.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            changes.Add(new ClaimChangeResponse(
                NormalizeDisplayPath(pair.Key),
                beforeSnapshot is not null && beforeSnapshot.TryGet(pair.Key, out _) ? "Updated" : "Added",
                ProjectPathValue(beforeSnapshot, pair.Key),
                pair.Value.Value));
        }

        foreach (var removed in commit.Removed.Order(StringComparer.Ordinal))
        {
            changes.Add(new ClaimChangeResponse(
                NormalizeDisplayPath(removed),
                "Removed",
                ProjectPathValue(beforeSnapshot, removed),
                null));
        }

        return changes
            .GroupBy(change => change.Path, StringComparer.Ordinal)
            .Select(group => MergeChanges(group))
            .OrderBy(change => change.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static ClaimChangeResponse MergeChanges(IEnumerable<ClaimChangeResponse> changes)
    {
        var ordered = changes.ToArray();
        var selected = ordered.FirstOrDefault(change => change.ChangeType == "Removed")
                       ?? ordered.FirstOrDefault(change => change.ChangeType == "Updated")
                       ?? ordered.First();

        return selected with
        {
            OldValue = ordered.FirstOrDefault(change => change.OldValue is not null)?.OldValue,
            NewValue = ordered.LastOrDefault(change => change.NewValue is not null)?.NewValue
        };
    }

    private static string NormalizeDisplayPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment is not "$type" and not "$id")
            .ToArray();
        return string.Join('/', segments);
    }

    private static object? ProjectPathValue(KVSnapshot? snapshot, string path)
    {
        if (snapshot is null)
        {
            return null;
        }

        return snapshot.TryGet(path, out var value) && value is not null
            ? value.Value
            : snapshot.ContainsPathOrDescendant(path) ? StructureValue : null;
    }

    private static object? ProjectPathValue(KVOverlay overlay, string path)
    {
        return overlay.TryGet(path, out var value) && value is not null
            ? value.Value
            : overlay.Keys.Any(key => KVPathIsSameOrDescendant(key, path)) ? StructureValue : null;
    }

    private static bool KVPathIsSameOrDescendant(string path, string ancestorPath)
    {
        if (string.IsNullOrWhiteSpace(ancestorPath))
        {
            return true;
        }

        return string.Equals(path, ancestorPath, StringComparison.Ordinal)
               || path.StartsWith(ancestorPath + "/", StringComparison.Ordinal);
    }

    private static ClaimDataResponse ProjectClaim(InsuranceClaim root)
    {
        return new ClaimDataResponse(
            root.ClaimNumber,
            root.Status,
            root.IncidentDate,
            root.Description,
            root.ClaimedTotal,
            new ClaimPolicyResponse(root.Policy.PolicyNumber, root.Policy.CoverageType),
            root.DamagedItems
                .Select(item => new DamagedItemResponse(root.DamagedItems.GetItemId(item), item.Description, item.EstimatedAmount))
                .ToArray(),
            root.Notes
                .Select(note => new ClaimNoteResponse(root.Notes.GetItemId(note), note.Text))
                .ToArray(),
            ProjectClaimant(root.Claimant));
    }

    private static ClaimantResponse? ProjectClaimant(Claimant? claimant)
    {
        return claimant switch
        {
            PersonClaimant person => new ClaimantResponse("PERSON", person.FullName),
            CompanyClaimant company => new ClaimantResponse("COMPANY", company.CompanyName),
            null => null,
            _ => new ClaimantResponse(claimant.GetType().Name, null)
        };
    }

    private static KVPatchOperation ToPatchOperation(ClaimPatchOperationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Path);

        var operation = request.OperationCode.ToUpperInvariant();
        return operation switch
        {
            KVPatchOperations.Add => new KVPatchOperation(operation, request.Path, ToAddPayload(request.Value)),
            KVPatchOperations.Move => new KVPatchOperation(operation, request.Path, ToMovePayload(request.Value)),
            KVPatchOperations.Init => new KVPatchOperation(operation, request.Path, ToRequiredString(request.Value, operation)),
            KVPatchOperations.Unset or KVPatchOperations.Discard or KVPatchOperations.Remove or KVPatchOperations.Drop => new KVPatchOperation(operation, request.Path),
            _ => new KVPatchOperation(operation, request.Path, ToScalarValue(request.Value))
        };
    }

    private static KVAddPatchPayload ToAddPayload(JsonElement? value)
    {
        return value?.Deserialize<KVAddPatchPayload>(PatchJsonOptions)
               ?? throw new InvalidOperationException("ADD patch operations require a KVAddPatchPayload value.");
    }

    private static object ToMovePayload(JsonElement? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("MOVE patch operations require a value.");
        }

        return value.Value.ValueKind == JsonValueKind.Number
            ? value.Value.GetInt32()
            : value.Value.Deserialize<KVMovePatchPayload>(PatchJsonOptions)!;
    }

    private static string ToRequiredString(JsonElement? value, string operation)
    {
        var result = value?.GetString();
        return !string.IsNullOrWhiteSpace(result)
            ? result
            : throw new InvalidOperationException($"{operation} patch operations require a non-empty string value.");
    }

    private static object? ToScalarValue(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        return NormalizeValue(value.Value);
    }

    private static void NormalizeOverlay(KVOverlay overlay)
    {
        NormalizeSnapshot(overlay.Snapshot);
    }

    private static void NormalizeSnapshot(KVSnapshot snapshot)
    {
        snapshot.Data = new Dictionary<string, KVValue>(snapshot.Data, StringComparer.Ordinal);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is not JsonElement json)
        {
            return value;
        }

        return json.ValueKind switch
        {
            JsonValueKind.String => json.GetString(),
            JsonValueKind.Number => json.TryGetDecimal(out var decimalValue) ? decimalValue : json.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => json.GetRawText()
        };
    }
}
