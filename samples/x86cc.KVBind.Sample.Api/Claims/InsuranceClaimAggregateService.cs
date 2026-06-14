using Marten;
using System.Text.Json;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api.Persistence;

namespace x86cc.KVBind.Sample.Api.Claims;

public sealed class InsuranceClaimAggregateService(
    IDocumentSession session,
    IKVDefinitionRegistry registry)
{
    private static readonly JsonSerializerOptions PatchJsonOptions = new(JsonSerializerDefaults.Web);
    private const string StructureValue = "{...}";

    // token → label maps keyed by schema path (GUIDs stripped), mirroring GetDefinitionSchema's option
    // sources, so change values render human-readably instead of as the stored allowed-value tokens.
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ValueLabels = BuildValueLabels();

    public async Task<ClaimSnapshotResponse> CreateClaimAsync(CreateClaimRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClaimNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.User);

        // Identity is the consumer's responsibility now — KVBind constructs no longer carry an aggregate id.
        var claimId = Guid.NewGuid();

        var snapshot = new KVSnapshot
        {
            CreatedBy = request.User,
            ModifiedBy = request.User
        };
        var overlay = KVOverlay.Create(snapshot.Clone(), request.User);
        // CreateNew materializes declared defaults (e.g. Status = "draft") into the overlay.
        var root = BindNew(overlay);

        // Only the mandatory identity is set at creation — everything else is edited on the draft page.
        root.ClaimNumber = request.ClaimNumber;

        var commit = root.CreateCommit(DateTimeOffset.UtcNow);
        var changes = ProjectCommitChanges(snapshot.Clone(), commit, BuildDisplayNames());
        snapshot.Apply(commit);

        session.Store(new ClaimSnapshotDocument
        {
            Id = claimId,
            Snapshot = snapshot
        });
        session.Store(new ClaimChangeSetDocument
        {
            Id = commit.CommitId,
            ClaimId = claimId,
            Commit = commit,
            Changes = changes
        });
        await session.SaveChangesAsync(cancellationToken);

        return ProjectSnapshot(claimId, snapshot);
    }

    public async Task<IReadOnlyList<ClaimSummaryResponse>> ListClaimsAsync(CancellationToken cancellationToken)
    {
        var documents = await session.Query<ClaimSnapshotDocument>().ToListAsync(cancellationToken);
        return documents
            .OrderBy(document => document.Snapshot.Modified)
            .Select(document =>
            {
                var root = Bind(KVOverlay.Create(document.Snapshot.Clone(), "system"));
                return new ClaimSummaryResponse(
                    document.Id,
                    root.ClaimNumber,
                    root.Status,
                    root.Priority,
                    root.Description,
                    root.ClaimedTotal,
                    document.Snapshot.LastCommitId ?? Guid.Empty,
                    document.Snapshot.LastCommitId,
                    document.Snapshot.Modified);
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

        return ProjectSnapshot(document.Id, document.Snapshot);
    }

    public async Task<ClaimDraftResponse?> OpenDraftAsync(Guid claimId, OpenClaimDraftRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.User);

        var snapshotDocument = await session.LoadAsync<ClaimSnapshotDocument>(claimId, cancellationToken);
        if (snapshotDocument is null)
        {
            return null;
        }

        // Resume an existing open draft for this user rather than silently discarding it. The draft is
        // keyed by (ClaimId, User), so a direct load resolves it — there is at most one.
        var existing = await session.LoadAsync<ClaimOverlayDocument>(
            ClaimOverlayDocument.DraftId(claimId, request.User), cancellationToken);

        if (existing is not null)
        {
            return ProjectDraft(existing, snapshotDocument.Snapshot);
        }

        var overlay = KVOverlay.Create(snapshotDocument.Snapshot.Clone(), request.User);
        var draft = ClaimOverlayDocument.Create(claimId, request.User, overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        return ProjectDraft(draft, snapshotDocument.Snapshot);
    }

    public async Task<ClaimDraftResponse?> GetDraftAsync(Guid claimId, Guid draftId, CancellationToken cancellationToken)
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

        // Auto-resync: an overlay with no draft changes and no rebase in progress can always be
        // fast-forwarded onto the latest snapshot — an empty overlay can never conflict.
        if (!draft.IsRebasing
            && draft.Changes.Count == 0
            && !IsDraftBasedOnLatestSnapshot(draft, snapshotDocument.Snapshot))
        {
            var emptyOverlay = draft.ToOverlay();
            emptyOverlay.Reset(snapshotDocument.Snapshot);
            draft.UpdateFrom(emptyOverlay);
            session.Store(draft);
            await session.SaveChangesAsync(cancellationToken);
        }

        return ProjectDraft(draft, snapshotDocument.Snapshot);
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

        var snapshotDocument = await session.LoadAsync<ClaimSnapshotDocument>(claimId, cancellationToken);
        if (snapshotDocument is null)
        {
            return null;
        }

        var overlay = draft.ToOverlay();
        var root = Bind(overlay);
        root.Patch(request.Select(ToPatchOperation));

        draft.UpdateFrom(overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        return ProjectDraft(draft, snapshotDocument.Snapshot);
    }

    // ── Rebase ──────────────────────────────────────────────────────────────────

    public async Task<RebaseResultResponse?> BeginRebaseAsync(Guid claimId, Guid draftId, CancellationToken cancellationToken)
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

        var overlay = draft.ToOverlay();

        // Idempotent: if a rebase is already in flight, return its current state instead of throwing.
        if (overlay.IsRebasing)
        {
            return new RebaseResultResponse(
                claimId, draftId,
                RebaseStateOutcome(overlay).ToString(),
                overlay.RebaseTarget?.LastCommitId ?? snapshotDocument.Snapshot.LastCommitId ?? Guid.Empty,
                overlay.Conflicts.Select(ProjectConflict).ToArray());
        }

        // The diff is driven by the commits made since the draft's base — fetch them and fold them into
        // "theirs" inside BeginRebase. The latest snapshot supplies the identity stamped on finish.
        var missingCommits = await LoadMissingCommitsAsync(claimId, overlay.Snapshot.LastCommitTimestamp, cancellationToken);

        var outcome = overlay.BeginRebase(snapshotDocument.Snapshot, missingCommits);
        draft.UpdateFrom(overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        return new RebaseResultResponse(
            claimId, draftId,
            outcome.ToString(),
            snapshotDocument.Snapshot.LastCommitId ?? Guid.Empty,
            overlay.Conflicts.Select(ProjectConflict).ToArray());
    }

    // Commits made after the draft's base, in chronological order. (Timestamp ordering assumes distinct
    // commit timestamps; a fully robust version would walk the PreviousCommitId chain from latest to base.)
    private async Task<IReadOnlyList<KVCommit>> LoadMissingCommitsAsync(
        Guid claimId, DateTimeOffset? baseTimestamp, CancellationToken cancellationToken)
    {
        var documents = await session.Query<ClaimChangeSetDocument>()
            .Where(document => document.ClaimId == claimId)
            .ToListAsync(cancellationToken);

        return documents
            .Where(document => baseTimestamp is null || document.Commit.Timestamp > baseTimestamp)
            .OrderBy(document => document.Commit.Timestamp)
            .Select(document => document.Commit)
            .ToArray();
    }

    private static KVRebaseOutcome RebaseStateOutcome(KVOverlay overlay) =>
        overlay.HasUnresolvedConflicts ? KVRebaseOutcome.HasUnresolvedConflicts : KVRebaseOutcome.CanAutomerge;

    public async Task<RebaseResultResponse?> ResolveRebaseConflictAsync(
        Guid claimId, Guid draftId, ResolveRebaseConflictRequest request, CancellationToken cancellationToken)
    {
        var draft = await session.LoadAsync<ClaimOverlayDocument>(draftId, cancellationToken);
        if (draft is null || draft.ClaimId != claimId || !draft.IsRebasing)
        {
            return null;
        }

        if (!Enum.TryParse<KVConflictResolution>(request.Resolution, ignoreCase: true, out var resolution)
            || resolution == KVConflictResolution.Unresolved)
        {
            throw new InvalidOperationException($"Invalid resolution '{request.Resolution}'. Use Ours, Theirs or Custom.");
        }

        var overlay = draft.ToOverlay();

        var customValue = resolution == KVConflictResolution.Custom ? ToConflictValue(request.Value) : null;
        overlay.ResolveConflict(request.Path, resolution, customValue);

        draft.UpdateFrom(overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        return new RebaseResultResponse(
            claimId, draftId,
            RebaseStateOutcome(overlay).ToString(),
            overlay.RebaseTarget?.LastCommitId ?? draft.BaseCommitId ?? Guid.Empty,
            overlay.Conflicts.Select(ProjectConflict).ToArray());
    }

    public async Task<ClaimDraftResponse?> FinishRebaseAsync(Guid claimId, Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await session.LoadAsync<ClaimOverlayDocument>(draftId, cancellationToken);
        if (draft is null || draft.ClaimId != claimId || !draft.IsRebasing)
        {
            return null;
        }

        var overlay = draft.ToOverlay();
        overlay.FinishRebase(); // throws if any conflict is unresolved

        draft.UpdateFrom(overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        var snapshotDocument = await session.LoadAsync<ClaimSnapshotDocument>(claimId, cancellationToken);
        return ProjectDraft(draft, snapshotDocument!.Snapshot);
    }

    public async Task<ClaimDraftResponse?> CancelRebaseAsync(Guid claimId, Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await session.LoadAsync<ClaimOverlayDocument>(draftId, cancellationToken);
        if (draft is null || draft.ClaimId != claimId || !draft.IsRebasing)
        {
            return null;
        }

        var overlay = draft.ToOverlay();
        overlay.CancelRebase();

        draft.UpdateFrom(overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        var snapshotDocument = await session.LoadAsync<ClaimSnapshotDocument>(claimId, cancellationToken);
        return ProjectDraft(draft, snapshotDocument!.Snapshot);
    }

    // Drops all draft changes and resyncs onto the latest snapshot — the "discard my changes" escape.
    public async Task<ClaimDraftResponse?> ResetDraftAsync(Guid claimId, Guid draftId, CancellationToken cancellationToken)
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

        var overlay = draft.ToOverlay();
        overlay.Reset(snapshotDocument.Snapshot);

        draft.UpdateFrom(overlay);
        session.Store(draft);
        await session.SaveChangesAsync(cancellationToken);

        return ProjectDraft(draft, snapshotDocument.Snapshot);
    }

    private static KVValue? ToConflictValue(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        var scalar = NormalizeValue(value.Value);
        return scalar is null ? null : KVValue.FromObject(scalar);
    }

    private static RebaseConflictResponse ProjectConflict(KVConflict conflict) => new(
        conflict.Path,
        conflict.Kind.ToString(),
        conflict.Resolution.ToString(),
        conflict.BaseValue?.Value,
        conflict.MainValue?.Value,
        conflict.OursValue?.Value,
        conflict.IsIncoming,
        conflict.RequiresResolution);

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

        if (!IsDraftBasedOnLatestSnapshot(draft, snapshotDocument.Snapshot))
        {
            return CommitClaimDraftResult.Stale(new StaleDraftResponse(
                claimId,
                draftId,
                draft.BaseCommitId ?? Guid.Empty,
                snapshotDocument.Snapshot.LastCommitId ?? Guid.Empty,
                draft.BaseCommitId,
                snapshotDocument.Snapshot.LastCommitId,
                "Draft is based on an older snapshot. Sync with master before committing."));
        }

        var overlay = draft.ToOverlay();
        var root = Bind(overlay);
        var commit = root.CreateCommit(DateTimeOffset.UtcNow);
        var changes = ProjectCommitChanges(snapshotDocument.Snapshot.Clone(), commit, BuildDisplayNames());

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
            ProjectSnapshot(snapshotDocument.Id, snapshotDocument.Snapshot)));
    }

    public async Task<IReadOnlyList<ClaimChangeSetResponse>> ListChangeSetsAsync(Guid claimId, CancellationToken cancellationToken)
    {
        var documents = await session.Query<ClaimChangeSetDocument>()
            .Where(document => document.ClaimId == claimId)
            .ToListAsync(cancellationToken);

        var displayNames = BuildDisplayNames();
        return documents
            .OrderByDescending(document => document.Commit.Timestamp)
            .Select(document => new ClaimChangeSetResponse(
                document.Commit.CommitId,
                document.Commit.PreviousCommitId,
                document.Commit.User,
                document.Commit.Timestamp,
                document.Commit.Changes.Keys.Where(k => document.Commit.Changes[k] != KVValue.Tombstone).Order(StringComparer.Ordinal).ToArray(),
                document.Commit.Changes.Keys.Where(k => document.Commit.Changes[k] == KVValue.Tombstone).Order(StringComparer.Ordinal).ToArray(),
                document.Changes.Count > 0
                    ? document.Changes
                    : ProjectCommitChanges(null, document.Commit, displayNames)))
            .ToArray();
    }

    private InsuranceClaim Bind(KVOverlay overlay)
    {
        return KVRootNode.Create<InsuranceClaim>(overlay, registry.Get<InsuranceClaim>());
    }

    // Binds a brand-new aggregate, materializing declared defaults into the overlay.
    private InsuranceClaim BindNew(KVOverlay overlay)
    {
        return KVRootNode.CreateNew<InsuranceClaim>(overlay, registry.Get<InsuranceClaim>());
    }

    // A draft is current iff its base commit is the snapshot's head commit (the commit chain is the anchor).
    private static bool IsDraftBasedOnLatestSnapshot(ClaimOverlayDocument draft, KVSnapshot latestSnapshot)
    {
        return draft.BaseCommitId == latestSnapshot.LastCommitId;
    }

    private ClaimSnapshotResponse ProjectSnapshot(Guid claimId, KVSnapshot snapshot)
    {
        var root = Bind(KVOverlay.Create(snapshot.Clone(), "system"));
        return new ClaimSnapshotResponse(
            claimId,
            ProjectClaim(root),
            snapshot.LastCommitId ?? Guid.Empty,
            snapshot.LastCommitId,
            snapshot.LastCommitTimestamp);
    }

    private ClaimDraftResponse ProjectDraft(ClaimOverlayDocument draft, KVSnapshot latestSnapshot)
    {
        var overlay = draft.ToOverlay();
        var root = Bind(overlay);

        var isStale = !draft.IsRebasing && !IsDraftBasedOnLatestSnapshot(draft, latestSnapshot);

        return new ClaimDraftResponse(
            draft.Id,
            draft.ClaimId,
            draft.User,
            ProjectClaim(root),
            overlay.BaseCommitId ?? Guid.Empty,
            overlay.BaseCommitId,
            ProjectOverlayChanges(overlay, root.GetAllChanges().Changes, BuildDisplayNames()),
            draft.IsRebasing,
            isStale,
            latestSnapshot.LastCommitId ?? Guid.Empty,
            draft.IsRebasing ? draft.RebaseTarget?.LastCommitId : null,
            draft.Conflicts.Select(ProjectConflict).ToArray());
    }

    private static IReadOnlyList<ClaimChangeResponse> ProjectOverlayChanges(KVOverlay overlay, IEnumerable<KVChangeDelta> changes, IReadOnlyDictionary<string, string>? displayNames = null)
    {
        var rows = new List<ClaimChangeResponse>();

        foreach (var change in changes)
        {
            var oldValue = ProjectPathValue(overlay.Snapshot, change.Path);
            var newValue = ProjectPathValue(overlay, change.Path);

            // A structural change (a new/removed collection item or group) projects to a single "{...}"
            // node. Expand it into its leaf fields — the same per-leaf shape commit changes have — so the
            // UI can render and drill into it instead of showing an opaque "{...}".
            var leaves = IsStructure(oldValue) || IsStructure(newValue)
                ? LeafPathsUnder(overlay, change.Path).ToArray()
                : [];

            if (leaves.Length > 0)
            {
                foreach (var leaf in leaves)
                    rows.Add(new ClaimChangeResponse(
                        NormalizeDisplayPath(leaf, displayNames),
                        change.ChangeType.ToString(),
                        ResolveValueLabel(leaf, ProjectPathValue(overlay.Snapshot, leaf)),
                        ResolveValueLabel(leaf, ProjectPathValue(overlay, leaf))));
            }
            else
            {
                rows.Add(new ClaimChangeResponse(
                    NormalizeDisplayPath(change.Path, displayNames),
                    change.ChangeType.ToString(),
                    ResolveValueLabel(change.Path, oldValue),
                    ResolveValueLabel(change.Path, newValue)));
            }
        }

        return rows
            .GroupBy(change => change.Path, StringComparer.Ordinal)
            .Select(group => MergeChanges(group))
            .OrderBy(change => change.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<ClaimChangeResponse> ProjectCommitChanges(KVSnapshot? beforeSnapshot, KVCommit commit, IReadOnlyDictionary<string, string>? displayNames = null)
    {
        var changes = new List<ClaimChangeResponse>();
        foreach (var pair in commit.Changes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (pair.Value == KVValue.Tombstone)
            {
                changes.Add(new ClaimChangeResponse(
                    NormalizeDisplayPath(pair.Key, displayNames),
                    "Removed",
                    ResolveValueLabel(pair.Key, ProjectPathValue(beforeSnapshot, pair.Key)),
                    null));
            }
            else
            {
                changes.Add(new ClaimChangeResponse(
                    NormalizeDisplayPath(pair.Key, displayNames),
                    beforeSnapshot is not null && beforeSnapshot.TryGet(pair.Key, out _) ? "Updated" : "Added",
                    ResolveValueLabel(pair.Key, ProjectPathValue(beforeSnapshot, pair.Key)),
                    ResolveValueLabel(pair.Key, pair.Value.Value)));
            }
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

    private static string NormalizeDisplayPath(string path, IReadOnlyDictionary<string, string>? displayNames = null)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment is not "$type" and not "$items")
            .ToArray();

        if (displayNames is null || displayNames.Count == 0)
            return string.Join('/', segments);

        var result = new string[segments.Length];
        var lookupPath = new System.Text.StringBuilder();
        foreach (var (i, segment) in segments.Select((s, i) => (i, s)))
        {
            if (Guid.TryParse(segment, out _))
            {
                result[i] = segment; // kept as full UUID; frontend renders as "Item #N"
            }
            else
            {
                if (lookupPath.Length > 0) lookupPath.Append('/');
                lookupPath.Append(segment);
                result[i] = displayNames.TryGetValue(lookupPath.ToString(), out var name) ? name : segment;
            }
        }
        return string.Join('/', result);
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
            : (overlay.Changes.Keys.Any(key => KVPathIsSameOrDescendant(key, path))
               || overlay.Snapshot.Data.Keys.Any(key => KVPathIsSameOrDescendant(key, path)))
              ? StructureValue : null;
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

    private static bool IsStructure(object? value) => value is string s && string.Equals(s, StructureValue, StringComparison.Ordinal);

    // The concrete leaf paths under a structural node (across draft + snapshot), excluding reserved
    // $items/$type segments, so a "{...}" item add/remove can be projected as its real fields.
    private static IEnumerable<string> LeafPathsUnder(KVOverlay overlay, string path)
    {
        var prefix = path + "/";
        return overlay.Changes.Keys
            .Concat(overlay.Snapshot.Data.Keys)
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal)
                          && !key.EndsWith("/$items", StringComparison.Ordinal)
                          && !key.EndsWith("/$type", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);
    }

    // Maps a stored allowed-value token (or array of tokens) to its human-readable label for the field at
    // this path; leaves the value untouched when the field has no allowed-value labels.
    private static object? ResolveValueLabel(string path, object? value)
    {
        if (value is null || !ValueLabels.TryGetValue(SchemaKey(path), out var labels))
        {
            return value;
        }

        if (value is string token)
        {
            return labels.TryGetValue(token, out var label) ? label : value;
        }

        if (value is System.Collections.IEnumerable sequence)
        {
            return sequence.Cast<object?>()
                .Select(element => element?.ToString() ?? string.Empty)
                .Select(t => labels.TryGetValue(t, out var label) ? label : t)
                .ToArray();
        }

        return value;
    }

    // Drops GUID and reserved segments so a concrete change path resolves to its schema key,
    // e.g. "DamagedItems/{guid}/Category" → "DamagedItems/Category".
    private static string SchemaKey(string path) =>
        string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment is not "$type" and not "$items" && !Guid.TryParse(segment, out _)));

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildValueLabels()
    {
        static IReadOnlyDictionary<string, string> Map(IEnumerable<(string Token, string Label)> pairs) =>
            pairs.ToDictionary(p => p.Token, p => p.Label, StringComparer.Ordinal);

        return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Status"] = Map(ClaimStatus.All.Select(s => (s.Id, s.Label))),
            ["Priority"] = Map(ClaimPriority.All.Select(p => (p.Id, p.Label))),
            ["Tags"] = Map(InsuranceClaimDefinitionBuilder.ClaimTags.Select(t => (t.Id, t.Label))),
            ["Policy/CoverageType"] = Map(
            [
                ("comprehensive", "Comprehensive"),
                ("collision", "Collision"),
                ("collision_plus", "Collision Plus"),
                ("liability", "Liability Only"),
                ("medical", "Medical Payments"),
            ]),
            ["DamagedItems/Category"] = Map(InsuranceClaimDefinitionBuilder.DamageCategories.Select(c => (c, ToLabel(c)))),
        };
    }

    private static ClaimDataResponse ProjectClaim(InsuranceClaim root)
    {
        return new ClaimDataResponse(
            root.ClaimNumber,
            root.Status,
            root.Priority,
            root.IncidentDate,
            root.Description,
            root.Tags,
            root.ClaimedTotal,
            new ClaimPolicyResponse(root.Policy.PolicyNumber, root.Policy.CoverageType),
            root.DamagedItems
                .Select(item => new DamagedItemResponse(root.DamagedItems.GetItemId(item), item.Description, item.Category, item.EstimatedAmount))
                .ToArray(),
            root.Notes
                .Select(note => new ClaimNoteResponse(root.Notes.GetItemId(note), note.Text))
                .ToArray(),
            ProjectClaimant(root.Claimant));
    }

    // Validates the draft using whichever profile the claim's current status selects.
    // No external profile parameter — the claim decides its own validation strictness.
    public async Task<ValidateDraftResponse?> ValidateDraftAsync(
        Guid claimId, Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await session.LoadAsync<ClaimOverlayDocument>(draftId, cancellationToken);
        if (draft is null || draft.ClaimId != claimId) return null;

        var overlay = draft.ToOverlay();
        var root = Bind(overlay);

        var result = root.Validate(); // profile determined by root.Status via GetValidationProfile()
        var profileName = root.Status is "in_review" or "approved" or "rejected" or "closed"
            ? "submit" : "draft";

        return new ValidateDraftResponse(
            profileName,
            result.Errors.Count == 0,
            result.Errors.Select(e => new ClaimValidationError(e.Path, e.Code, e.Message)).ToArray());
    }

    public static ClaimSchemaResponse GetSchema() => new(
        ClaimStatus.All.Select(s => s.Id).ToArray(),
        ClaimPriority.All.Select(p => p.Id).ToArray(),
        ["comprehensive", "collision", "collision_plus", "liability", "medical"],
        [.. InsuranceClaimDefinitionBuilder.DamageCategories]);

    private static AllowedValueOption V(string id, string label) => new(id, label, null, null);
    private static AllowedValueOption VC(string id, string label, string template, IReadOnlyList<PlaceholderMeta> placeholders)
        => new(id, label, template, placeholders);

    // Builds a path → DisplayName lookup from the actual definition so the schema's labels are driven by the
    // DSL (DisplayName(...) / [KVBind(DisplayName=...)]) rather than duplicated here. Falls back to the
    // hand-authored label when the definition declares none.
    private IReadOnlyDictionary<string, string> BuildDisplayNames()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        Collect(registry.Get<InsuranceClaim>(), prefix: string.Empty);
        return map;

        void Collect(KVNodeDefinition node, string prefix)
        {
            foreach (var field in node.Fields)
                Put(prefix, field.SubSegmentPath, field.DisplayName);

            foreach (var group in node.Nodes)
            {
                Put(prefix, group.SubSegmentPath, group.DisplayName);
                Collect(group, Combine(prefix, group.SubSegmentPath));
            }

            foreach (var collection in node.Collections)
            {
                Put(prefix, collection.SubSegmentPath, collection.DisplayName);
                foreach (var item in collection.ItemDefinitionsByToken.Values)
                    Collect(item.NodeDefinition, Combine(prefix, collection.SubSegmentPath));
            }

            foreach (var nested in node.NestedNodes)
            {
                Put(prefix, nested.SubSegmentPath, nested.DisplayName);
                foreach (var type in nested.TypeDefinitionsByToken.Values)
                    Collect(type.NodeDefinition, Combine(prefix, nested.SubSegmentPath));
            }
        }

        void Put(string prefix, string segment, string? displayName)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                map[Combine(prefix, segment)] = displayName!;
        }

        static string Combine(string prefix, string segment) =>
            string.IsNullOrEmpty(prefix) ? segment : $"{prefix}/{segment}";
    }

    // Reads each field's consumer "ui:control" annotation into a path → control map, so the schema's
    // control hints are driven by the layout DSL (.UiControl(...)) instead of hard-coded here.
    private IReadOnlyDictionary<string, string> BuildUiControls()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        Collect(registry.Get<InsuranceClaim>(), prefix: string.Empty);
        return map;

        void Collect(KVNodeDefinition node, string prefix)
        {
            foreach (var field in node.Fields)
                if (field.ControlOf() is { } control)
                    map[Combine(prefix, field.SubSegmentPath)] = control;

            foreach (var group in node.Nodes)
                Collect(group, Combine(prefix, group.SubSegmentPath));

            foreach (var collection in node.Collections)
                foreach (var item in collection.ItemDefinitionsByToken.Values)
                    Collect(item.NodeDefinition, Combine(prefix, collection.SubSegmentPath));

            foreach (var nested in node.NestedNodes)
                foreach (var type in nested.TypeDefinitionsByToken.Values)
                    Collect(type.NodeDefinition, Combine(prefix, nested.SubSegmentPath));
        }

        static string Combine(string prefix, string segment) =>
            string.IsNullOrEmpty(prefix) ? segment : $"{prefix}/{segment}";
    }

    public DefinitionSchemaResponse GetDefinitionSchema()
    {
        var names = BuildDisplayNames();
        string L(string key, string fallback) => names.TryGetValue(key, out var n) ? n : fallback;

        // Control hints come from each field's "ui:control" annotation, falling back to a default.
        var controls = BuildUiControls();
        string Ui(string key, string fallback) => controls.TryGetValue(key, out var c) ? c : fallback;

        return new(
            Fields:
            [
                new("ClaimNumber",  L("ClaimNumber", "Claim Number"),   "string",  Ui("ClaimNumber", "text"),   true,  null),
                new("Status",       L("Status", "Status"),              "string",  Ui("Status", "select"),      true,  ClaimStatus.All.Select(s => V(s.Id, s.Label)).ToArray()),
                new("Priority",     L("Priority", "Priority"),          "string",  Ui("Priority", "radio"),     false, ClaimPriority.All.Select(p => V(p.Id, p.Label)).ToArray()),
                new("IncidentDate", L("IncidentDate", "Incident Date"), "date",    Ui("IncidentDate", "date"),  false, null),
                new("Description",  L("Description", "Description"),     "string",  Ui("Description", "textarea"), false, null),
                new("Tags",         L("Tags", "Tags"),                  "string",  Ui("Tags", "multiselect"), false,
                    InsuranceClaimDefinitionBuilder.ClaimTags.Select(t => V(t.Id, t.Label)).ToArray()),
            ],
            FieldGroups:
            [
                new("Policy", L("Policy", "Policy"),
                [
                    new("PolicyNumber", L("Policy/PolicyNumber", "Policy Number"), "string", "text",   false, null),
                    new("CoverageType", L("Policy/CoverageType", "Coverage Type"), "string", Ui("Policy/CoverageType", "select"), true,
                    [
                        V("comprehensive",  "Comprehensive"),
                        VC("collision",      "Collision",
                            "Collision — {Deductible:C} deductible",
                            [new("Deductible", "Deductible Amount", "decimal")]),
                        VC("collision_plus", "Collision Plus",
                            "Collision Plus — {Deductible:C} deductible, {ExcessAmount:C} excess",
                            [new("Deductible", "Deductible Amount", "decimal"), new("ExcessAmount", "Excess Amount", "decimal")]),
                        V("liability",      "Liability Only"),
                        V("medical",        "Medical Payments"),
                    ]),
                ]),
            ],
            Collections:
            [
                new("DamagedItems", L("DamagedItems", "Damaged Items"),
                [
                    new("DamagedItem", "Damaged Item",
                    [
                        new("Description",     L("DamagedItems/Description", "Description"),         "string",  "text",   true,  null),
                        new("Category",        L("DamagedItems/Category", "Category"),               "string",  Ui("DamagedItems/Category", "select"), false, InsuranceClaimDefinitionBuilder.DamageCategories.Select(c => V(c, ToLabel(c))).ToArray()),
                        new("EstimatedAmount", L("DamagedItems/EstimatedAmount", "Estimated Amount"),"decimal", Ui("DamagedItems/EstimatedAmount", "number"), false, null),
                    ]),
                ]),
                new("Notes", L("Notes", "Notes"),
                [
                    new("Note", "Note", [new("Text", L("Notes/Text", "Note text"), "string", "text", false, null)]),
                ]),
            ],
            NestedNodes:
            [
                new("Claimant", L("Claimant", "Claimant"),
                [
                    new("PERSON",  "Person",  [new("FullName",    L("Claimant/FullName", "Full name"),       "string", "text", true,  null)]),
                    new("COMPANY", "Company", [new("CompanyName", L("Claimant/CompanyName", "Company name"), "string", "text", true,  null)]),
                ]),
            ]);
    }

    private static string ToLabel(string snake) =>
        string.Concat(snake.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]))
              .Replace("_", " ", StringComparison.Ordinal);

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
