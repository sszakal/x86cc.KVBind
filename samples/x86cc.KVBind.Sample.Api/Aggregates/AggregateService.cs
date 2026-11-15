using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Marten;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api.Diagnostics;
using x86cc.KVBind.Sample.Api.Persistence;

namespace x86cc.KVBind.Sample.Api.Aggregates;

/// <summary>
/// The draft / commit / rebase workflow for any model in the <see cref="AggregateCatalog"/>. Nothing here
/// knows a model's fields: data is projected by walking the definition, patches are KVBind patch
/// operations, and the model supplies only what the definition can't (see <see cref="IAggregateModel"/>).
/// Every stage is measured through <see cref="RequestTimings"/>.
/// </summary>
public sealed class AggregateService(IDocumentSession session, RequestTimings timings)
{
    /// <summary>The largest page of change sets one request returns.</summary>
    public const int MaxChangeSetPage = 500;

    private const string StructureValue = "{...}";

    private static readonly JsonSerializerOptions PatchJsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions EventJsonOptions = CreateEventJsonOptions();

    // ── Aggregates ───────────────────────────────────────────────────────────────

    public async Task<AggregateSnapshotResponse> CreateAsync(IAggregateModel model, CreateAggregateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ApiRequestException.RequireText(request.User, "user");

        var id = Guid.NewGuid();
        KVSnapshot? parentSnapshot = null;
        KVRootNode root;

        if (request.ParentId is { } parentId)
        {
            if (!model.SupportsChildren)
            {
                throw new ApiRequestException($"Model '{model.Name}' does not support child aggregates.");
            }

            var parent = await this.LoadSnapshotAsync(model, parentId, cancellationToken)
                         ?? throw new ApiRequestException($"Parent '{parentId}' was not found.");
            if (parent.ParentId is not null)
            {
                throw new ApiRequestException("A child aggregate cannot itself be a parent in this sample.");
            }

            parentSnapshot = parent.Snapshot;
            var parentRoot = timings.Measure("bind", () => model.Bind(KVOverlay.Create(parent.Snapshot.Clone(), request.User), null));
            root = timings.Measure("bind", () => ApiRequestException.Run(() => model.CreateChild(parentRoot)));
        }
        else
        {
            // Mint the snapshot via the definition so it is born stamped at the current schema version.
            var newSnapshot = model.Definition.NewSnapshot();
            root = timings.Measure("bind", () => model.BindNew(KVOverlay.Create(newSnapshot, request.User)));
        }

        var overlay = root.Model.Overlay;
        var snapshot = overlay.Snapshot.Clone();
        snapshot.CreatedBy = request.User;
        snapshot.ModifiedBy = request.User;

        if (request.Values is { Count: > 0 } values)
        {
            timings.Measure("patch", () => ApiRequestException.Run(() => root.Patch(values.Select(pair =>
                ToPatchOperation(new PatchOperationRequest(KVPatchOperations.Set, "/" + pair.Key.TrimStart('/'), pair.Value))))));
        }

        var commit = timings.Measure("commit", () => root.CreateCommit(DateTimeOffset.UtcNow));
        commit.User = request.User;
        var changes = timings.Measure("project", () => ProjectCommitChanges(model.Schema, snapshot.Clone(), commit));
        snapshot.Apply(commit);

        var events = ToEventRecords(root.DequeueEvents());
        var document = new AggregateSnapshotDocument { Id = id, Model = model.Name, ParentId = request.ParentId, Snapshot = snapshot };
        session.Store(document);
        session.Store(new AggregateChangeSetDocument
        {
            Id = commit.CommitId,
            Model = model.Name,
            AggregateId = id,
            Commit = commit,
            Changes = [.. changes],
            Events = events
        });
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return this.ProjectSnapshot(model, document, parentSnapshot);
    }

    public async Task<IReadOnlyList<AggregateSummaryResponse>> ListAsync(IAggregateModel model, CancellationToken cancellationToken)
    {
        var documents = await timings.MeasureAsync("load", () => session.Query<AggregateSnapshotDocument>()
            .Where(document => document.Model == model.Name)
            .ToListAsync(cancellationToken));

        return timings.Measure("project", () => documents
            .OrderByDescending(document => document.Snapshot.Modified)
            .Select(document =>
            {
                var summary = new JsonObject();
                foreach (var path in model.SummaryFields)
                {
                    summary[path] = document.Snapshot.TryGet(path, out var value) && value is not null
                        ? AggregateDataProjector.ToJson(value.Value)
                        : null;
                }

                return new AggregateSummaryResponse(
                    document.Id,
                    document.ParentId,
                    summary,
                    document.Snapshot.LastCommitId,
                    document.Snapshot.Modified,
                    document.Snapshot.ModifiedBy,
                    document.Snapshot.SchemaVersion);
            })
            .ToArray());
    }

    public async Task<AggregateSnapshotResponse?> GetSnapshotAsync(IAggregateModel model, Guid id, CancellationToken cancellationToken)
    {
        var document = await this.LoadSnapshotAsync(model, id, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var parentSnapshot = await this.LoadParentSnapshotAsync(model, document, cancellationToken);
        return this.ProjectSnapshot(model, document, parentSnapshot);
    }

    /// <summary>
    /// One page of an aggregate's commits, newest first. Long histories (thousands of commits) are read a
    /// page at a time instead of whole.
    /// </summary>
    public async Task<ChangeSetPageResponse> ListChangeSetsAsync(IAggregateModel model, Guid id, int offset, int limit, CancellationToken cancellationToken)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, MaxChangeSetPage);
        var query = session.Query<AggregateChangeSetDocument>()
            .Where(document => document.AggregateId == id && document.Model == model.Name);
        var total = await timings.MeasureAsync("load", () => query.CountAsync(cancellationToken));
        var documents = await timings.MeasureAsync("load", () => query
            .OrderByDescending(document => document.Commit.Timestamp)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken));

        var items = documents
            .Select(document => new ChangeSetResponse(
                document.Commit.CommitId,
                document.Commit.PreviousCommitId,
                document.Commit.User,
                document.Commit.Timestamp,
                document.Commit.MigrationToVersion,
                document.Changes,
                document.Events))
            .ToArray();
        return new ChangeSetPageResponse(items, total, offset, limit);
    }

    /// <summary>
    /// Stores an aggregate exactly as an older release would have (no migration, no binding), returning its id.
    /// Reading it — or the batch migration — then runs the model's migrations.
    /// </summary>
    public async Task<AggregateSummaryResponse> CreateLegacyAsync(IAggregateModel model, string user, CancellationToken cancellationToken)
    {
        ApiRequestException.RequireText(user, "user");
        var factory = model.LegacySnapshot ?? throw new ApiRequestException($"Model '{model.Name}' has no legacy sample.");
        var document = new AggregateSnapshotDocument { Id = Guid.NewGuid(), Model = model.Name, Snapshot = factory(user) };
        session.Store(document);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));
        return new AggregateSummaryResponse(
            document.Id, null, new JsonObject(), null, document.Snapshot.Modified, user, document.Snapshot.SchemaVersion);
    }

    /// <summary>Brings every stored aggregate of the model up to the current schema version.</summary>
    public async Task<MigrationBatchResponse> MigrateAllAsync(IAggregateModel model, CancellationToken cancellationToken)
    {
        var documents = await timings.MeasureAsync("load", () => session.Query<AggregateSnapshotDocument>()
            .Where(document => document.Model == model.Name)
            .ToListAsync(cancellationToken));

        var behind = documents.Where(document => document.Snapshot.SchemaVersion < model.Definition.CurrentSchemaVersion).ToList();
        var subjects = behind.Select(document => new KVMigrationSubject { Key = document.Id, Snapshot = document.Snapshot }).ToList();

        // One batch: each migration's prepare phase (if any) runs once over the aggregates that still need it.
        var results = await timings.MeasureAsync("migrate", () =>
            KVMigrator.MigrateBatchAsync(subjects, model.Definition, cancellationToken: cancellationToken));

        var commitsById = results.ToDictionary(result => (Guid)result.Key, result => result.Commits);
        var commits = 0;
        foreach (var document in behind)
        {
            foreach (var commit in commitsById.GetValueOrDefault(document.Id, []))
            {
                this.StoreMigrationCommit(model, document, commit);
                commits++;
            }

            session.Store(document);
        }

        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));
        return new MigrationBatchResponse(documents.Count, behind.Count, commits, model.Definition.CurrentSchemaVersion);
    }

    // ── Drafts ───────────────────────────────────────────────────────────────────

    public async Task<AggregateDraftResponse?> OpenDraftAsync(IAggregateModel model, Guid id, OpenDraftRequest request, CancellationToken cancellationToken)
    {
        ApiRequestException.RequireText(request.User, "user");

        var snapshotDocument = await this.LoadSnapshotAsync(model, id, cancellationToken);
        if (snapshotDocument is null)
        {
            return null;
        }

        var parentSnapshot = await this.LoadParentSnapshotAsync(model, snapshotDocument, cancellationToken);

        // Resume an existing open draft for this user rather than silently discarding it.
        var existing = await timings.MeasureAsync("load", () =>
            session.LoadAsync<AggregateDraftDocument>(AggregateDraftDocument.DraftId(id, request.User), cancellationToken));
        if (existing is not null)
        {
            return this.ProjectDraft(model, existing, snapshotDocument, parentSnapshot);
        }

        var draft = AggregateDraftDocument.Create(model.Name, id, request.User, KVOverlay.Create(snapshotDocument.Snapshot.Clone(), request.User));
        session.Store(draft);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return this.ProjectDraft(model, draft, snapshotDocument, parentSnapshot);
    }

    public async Task<AggregateDraftResponse?> GetDraftAsync(IAggregateModel model, Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (draft, snapshotDocument, parentSnapshot) = context.Value;

        // Auto-resync: an overlay with no draft changes and no rebase in progress can always be fast-forwarded
        // onto the latest snapshot — an empty overlay can never conflict.
        if (!draft.IsRebasing && draft.Changes.Count == 0 && !IsDraftCurrent(draft, snapshotDocument.Snapshot))
        {
            var emptyOverlay = draft.ToOverlay();
            emptyOverlay.Reset(snapshotDocument.Snapshot);
            draft.UpdateFrom(emptyOverlay);
            session.Store(draft);
            await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));
        }

        return this.ProjectDraft(model, draft, snapshotDocument, parentSnapshot);
    }

    public async Task<AggregateDraftResponse?> PatchDraftAsync(
        IAggregateModel model, Guid id, Guid draftId, IReadOnlyList<PatchOperationRequest> request, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (draft, snapshotDocument, parentSnapshot) = context.Value;
        if (draft.IsRebasing)
        {
            throw new ApiRequestException("The draft is being rebased; finish or cancel the rebase before editing.");
        }

        var operations = ApiRequestException.Run(() => request.Select(ToPatchOperation).ToArray());
        var overlay = draft.ToOverlay();
        var root = timings.Measure("bind", () => model.Bind(overlay, parentSnapshot));
        timings.Measure("patch", () => ApiRequestException.Run(() => root.Patch(operations)));

        draft.UpdateFrom(overlay);
        draft.PendingEvents.AddRange(ToEventRecords(root.DequeueEvents()));
        session.Store(draft);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return this.ProjectDraft(model, draft, snapshotDocument, parentSnapshot, root);
    }

    public async Task<ValidateDraftResponse?> ValidateDraftAsync(IAggregateModel model, Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (draft, _, parentSnapshot) = context.Value;
        var root = timings.Measure("bind", () => model.Bind(draft.ToOverlay(), parentSnapshot));
        return timings.Measure("validate", () => Validate(model, root));
    }

    // Drops all draft changes and resyncs onto the latest snapshot — the "discard my changes" escape.
    public async Task<AggregateDraftResponse?> ResetDraftAsync(IAggregateModel model, Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (draft, snapshotDocument, parentSnapshot) = context.Value;
        var overlay = draft.ToOverlay();
        overlay.Reset(snapshotDocument.Snapshot);
        draft.UpdateFrom(overlay);
        draft.PendingEvents.Clear(); // nothing they announced happened
        session.Store(draft);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return this.ProjectDraft(model, draft, snapshotDocument, parentSnapshot);
    }

    public async Task<CommitResult?> CommitDraftAsync(
        IAggregateModel model, Guid id, Guid draftId, CommitDraftRequest request, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (draft, snapshotDocument, parentSnapshot) = context.Value;
        if (draft.IsRebasing || !IsDraftCurrent(draft, snapshotDocument.Snapshot))
        {
            return CommitResult.Stale(new StaleDraftResponse(
                id,
                draftId,
                draft.BaseCommitId,
                snapshotDocument.Snapshot.LastCommitId,
                "Draft is based on an older snapshot. Rebase onto the latest commit before committing."));
        }

        var overlay = draft.ToOverlay();
        var root = timings.Measure("bind", () => model.Bind(overlay, parentSnapshot));

        KVCommit commit;
        try
        {
            commit = timings.Measure("commit", () => root.CreateCommit(DateTimeOffset.UtcNow));
        }
        catch (KVChangeSetValidationException)
        {
            return CommitResult.Rejected(Validate(model, root));
        }

        var user = string.IsNullOrWhiteSpace(request.User) ? draft.User : request.User;
        commit.User = user;
        var changes = timings.Measure("project", () => ProjectCommitChanges(model.Schema, snapshotDocument.Snapshot.Clone(), commit));
        snapshotDocument.Snapshot.Apply(commit);

        var events = draft.PendingEvents.Concat(ToEventRecords(root.DequeueEvents())).ToList();
        session.Store(snapshotDocument);
        session.Store(new AggregateChangeSetDocument
        {
            Id = commit.CommitId,
            Model = model.Name,
            AggregateId = id,
            Commit = commit,
            Changes = [.. changes],
            Events = events
        });
        session.Delete<AggregateDraftDocument>(draftId);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return CommitResult.Committed(new CommitResponse(
            id,
            draftId,
            commit.CommitId,
            this.ProjectSnapshot(model, snapshotDocument, parentSnapshot),
            events));
    }

    // ── Rebase ───────────────────────────────────────────────────────────────────

    public async Task<RebaseResultResponse?> BeginRebaseAsync(IAggregateModel model, Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null)
        {
            return null;
        }

        var (draft, snapshotDocument, _) = context.Value;
        var overlay = draft.ToOverlay();

        // Idempotent: if a rebase is already in flight, return its current state instead of throwing.
        if (overlay.IsRebasing)
        {
            return new RebaseResultResponse(
                id,
                draftId,
                RebaseStateOutcome(overlay).ToString(),
                overlay.RebaseTarget?.LastCommitId ?? snapshotDocument.Snapshot.LastCommitId,
                overlay.Conflicts.Select(conflict => ProjectConflict(model.Schema, conflict)).ToArray());
        }

        // The diff is driven by the commits made since the draft's base — fetch them and fold them into
        // "theirs" inside BeginRebase. The latest snapshot supplies the identity stamped on finish.
        var missingCommits = await this.LoadCommitsAfterAsync(model, id, overlay.Snapshot.LastCommitId, overlay.Snapshot.LastCommitTimestamp, snapshotDocument.Snapshot.LastCommitId, cancellationToken);

        var outcome = timings.Measure("rebase", () => overlay.BeginRebase(snapshotDocument.Snapshot, missingCommits));

        // Derived values are recomputed when the rebase finishes, so a conflict on one is no real conflict.
        foreach (var conflict in overlay.Conflicts.Where(c => c.RequiresResolution && IsDerived(model, c.Path)).ToArray())
        {
            overlay.ResolveConflict(conflict.Path, KVConflictResolution.Theirs);
        }

        if (outcome == KVRebaseOutcome.HasUnresolvedConflicts && !overlay.HasUnresolvedConflicts)
        {
            outcome = KVRebaseOutcome.CanAutomerge;
        }

        draft.UpdateFrom(overlay);
        session.Store(draft);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return new RebaseResultResponse(
            id,
            draftId,
            outcome.ToString(),
            snapshotDocument.Snapshot.LastCommitId,
            overlay.Conflicts.Select(conflict => ProjectConflict(model.Schema, conflict)).ToArray());
    }

    public async Task<RebaseResultResponse?> ResolveRebaseConflictAsync(
        IAggregateModel model, Guid id, Guid draftId, ResolveRebaseConflictRequest request, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null || !context.Value.Draft.IsRebasing)
        {
            return null;
        }

        var draft = context.Value.Draft;
        if (!Enum.TryParse<KVConflictResolution>(request.Resolution, ignoreCase: true, out var resolution)
            || resolution == KVConflictResolution.Unresolved)
        {
            throw new ApiRequestException($"Invalid resolution '{request.Resolution}'. Use Ours, Theirs or Custom.");
        }

        var customValue = resolution == KVConflictResolution.Custom ? ToConflictValue(request.Value) : null;
        if (resolution == KVConflictResolution.Custom && customValue is null)
        {
            // Reject now instead of letting finish fail later with "Custom resolution ... has no value".
            throw new ApiRequestException($"A Custom resolution for '{request.Path}' needs a non-null value.");
        }

        var overlay = draft.ToOverlay();
        ApiRequestException.Run(() => overlay.ResolveConflict(request.Path, resolution, customValue));
        draft.UpdateFrom(overlay);
        session.Store(draft);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return new RebaseResultResponse(
            id,
            draftId,
            RebaseStateOutcome(overlay).ToString(),
            overlay.RebaseTarget?.LastCommitId ?? draft.BaseCommitId,
            overlay.Conflicts.Select(conflict => ProjectConflict(model.Schema, conflict)).ToArray());
    }

    public async Task<AggregateDraftResponse?> FinishRebaseAsync(IAggregateModel model, Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null || !context.Value.Draft.IsRebasing)
        {
            return null;
        }

        var (draft, snapshotDocument, parentSnapshot) = context.Value;
        var overlay = draft.ToOverlay();
        timings.Measure("rebase", () => ApiRequestException.Run(overlay.FinishRebase)); // rejected while a conflict is unresolved

        // Rebase runs no change reactions: recompute derived values against the merged data.
        var root = timings.Measure("bind", () => model.Bind(overlay, parentSnapshot));
        timings.Measure("react", () => root.NotifyReplaced());

        draft.UpdateFrom(overlay);
        draft.PendingEvents.AddRange(ToEventRecords(root.DequeueEvents()));
        session.Store(draft);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return this.ProjectDraft(model, draft, snapshotDocument, parentSnapshot, root);
    }

    public async Task<AggregateDraftResponse?> CancelRebaseAsync(IAggregateModel model, Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        var context = await this.LoadDraftContextAsync(model, id, draftId, cancellationToken);
        if (context is null || !context.Value.Draft.IsRebasing)
        {
            return null;
        }

        var (draft, snapshotDocument, parentSnapshot) = context.Value;
        var overlay = draft.ToOverlay();
        overlay.CancelRebase();
        draft.UpdateFrom(overlay);
        session.Store(draft);
        await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));

        return this.ProjectDraft(model, draft, snapshotDocument, parentSnapshot);
    }

    // ── Rebase and history helpers ──────────────────────────────────────────────────

    // A derived path is a schema path ("TotalCost"), or "**/Key" for that field at any depth, as in a
    // recursive tree where the same total sits on every level.
    private static bool IsDerived(IAggregateModel model, string path)
    {
        var schemaPath = model.Schema.ToSchemaPath(path);
        foreach (var derived in model.DerivedPaths)
        {
            if (derived.StartsWith("**/", StringComparison.Ordinal)
                ? schemaPath == derived[3..] || schemaPath.EndsWith(derived[2..], StringComparison.Ordinal)
                : schemaPath == derived)
            {
                return true;
            }
        }

        return false;
    }

    // Walks back from the latest commit to the base. A missing commit on the way gives null when strict, else
    // ends the walk there.
    private static List<KVCommit>? Chain(IReadOnlyList<AggregateChangeSetDocument> documents, Guid? baseCommitId, Guid? latestCommitId, bool strict)
    {
        var byId = documents.ToDictionary(document => document.Commit.CommitId, document => document.Commit);
        var chain = new List<KVCommit>();
        var cursor = latestCommitId;
        while (cursor is { } commitId && commitId != baseCommitId)
        {
            if (!byId.TryGetValue(commitId, out var commit))
            {
                if (strict)
                {
                    return null;
                }

                break;
            }

            chain.Add(commit);
            cursor = commit.PreviousCommitId;
        }

        chain.Reverse();
        return chain;
    }

    // ── Projection ───────────────────────────────────────────────────────────────

    private static ValidateDraftResponse Validate(IAggregateModel model, KVRootNode root)
    {
        var result = root.Validate();
        return new ValidateDraftResponse(
            model.ProfileName(root),
            result.Errors.Count == 0,
            result.Errors
                .Select(error => new ValidationErrorResponse(error.Path, model.Schema.ToDisplayPath(error.Path), error.Code, error.Message ?? error.Code))
                .ToArray());
    }

    private static IReadOnlyList<AggregateChangeRow> ProjectOverlayChanges(ModelSchema schema, KVOverlay overlay, IEnumerable<KVChangeDelta> changes)
    {
        var rows = new List<AggregateChangeRow>();
        KeyIndex? snapshotKeys = null;
        KeyIndex? allKeys = null;

        foreach (var change in changes)
        {
            // A field holds a value, never a structure, so it needs no look at its descendants.
            if (schema.IsField(change.Path))
            {
                rows.Add(new AggregateChangeRow(
                    schema.ToDisplayPath(change.Path),
                    change.ChangeType.ToString(),
                    schema.LabelValue(change.Path, overlay.Snapshot.TryGet(change.Path, out var before) ? before?.Value : null),
                    schema.LabelValue(change.Path, overlay.TryGet(change.Path, out var after) ? after?.Value : null)));
                continue;
            }

            // Built on the first structural change only: sorted keys turn "what is under this path" into a
            // binary search instead of a scan of every key per change.
            snapshotKeys ??= new KeyIndex(overlay.Snapshot.Data.Keys);
            allKeys ??= new KeyIndex(overlay.Changes.Keys.Concat(overlay.Snapshot.Data.Keys));

            var oldValue = ProjectPathValue(overlay.Snapshot, snapshotKeys, change.Path);
            var newValue = ProjectPathValue(overlay, allKeys, change.Path);

            // A structural change (a new/removed collection item or group) projects to a single "{...}" node.
            // Expand it into its leaf fields — the shape commit changes have — so the UI can drill into it.
            var leaves = IsStructure(oldValue) || IsStructure(newValue)
                ? allKeys.Under(change.Path).Where(IsLeafKey).ToArray()
                : [];

            if (leaves.Length > 0)
            {
                rows.AddRange(leaves.Select(leaf => new AggregateChangeRow(
                    schema.ToDisplayPath(leaf),
                    change.ChangeType.ToString(),
                    schema.LabelValue(leaf, ProjectPathValue(overlay.Snapshot, snapshotKeys, leaf)),
                    schema.LabelValue(leaf, ProjectPathValue(overlay, allKeys, leaf)))));
            }
            else
            {
                rows.Add(new AggregateChangeRow(
                    schema.ToDisplayPath(change.Path),
                    change.ChangeType.ToString(),
                    schema.LabelValue(change.Path, oldValue),
                    schema.LabelValue(change.Path, newValue)));
            }
        }

        return MergeByPath(rows);
    }

    private static IReadOnlyList<AggregateChangeRow> ProjectCommitChanges(ModelSchema schema, KVSnapshot? beforeSnapshot, KVCommit commit)
    {
        var rows = new List<AggregateChangeRow>();
        var beforeKeys = beforeSnapshot is null ? null : new KeyIndex(beforeSnapshot.Data.Keys);
        foreach (var (path, value) in commit.Changes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (path.EndsWith("/$items", StringComparison.Ordinal) || path == "$items")
            {
                continue; // item order is structure, shown through the items themselves
            }

            var before = ProjectPathValue(beforeSnapshot, beforeKeys, path);
            if (value == KVValue.Tombstone)
            {
                rows.Add(new AggregateChangeRow(schema.ToDisplayPath(path), "Removed", schema.LabelValue(path, before), null));
            }
            else
            {
                rows.Add(new AggregateChangeRow(
                    schema.ToDisplayPath(path),
                    beforeSnapshot is not null && beforeSnapshot.TryGet(path, out _) ? "Updated" : "Added",
                    schema.LabelValue(path, before),
                    schema.LabelValue(path, value.Value)));
            }
        }

        return MergeByPath(rows);
    }

    private static IReadOnlyList<AggregateChangeRow> MergeByPath(IEnumerable<AggregateChangeRow> rows) =>
        rows.GroupBy(row => row.Path, StringComparer.Ordinal)
            .Select(group =>
            {
                var ordered = group.ToArray();
                var selected = Array.Find(ordered, row => row.ChangeType == "Removed")
                               ?? Array.Find(ordered, row => row.ChangeType == "Updated")
                               ?? ordered[0];
                return selected with
                {
                    OldValue = Array.Find(ordered, row => row.OldValue is not null)?.OldValue,
                    NewValue = Array.FindLast(ordered, row => row.NewValue is not null)?.NewValue
                };
            })
            .OrderBy(row => row.Path, StringComparer.Ordinal)
            .ToArray();

    private static object? ProjectPathValue(KVSnapshot? snapshot, KeyIndex? keys, string path)
    {
        if (snapshot is null)
        {
            return null;
        }

        if (snapshot.TryGet(path, out var value) && value is not null)
        {
            return value.Value;
        }

        return keys!.Under(path).Any() ? StructureValue : null;
    }

    private static object? ProjectPathValue(KVOverlay overlay, KeyIndex keys, string path)
    {
        if (overlay.TryGet(path, out var value) && value is not null)
        {
            return value.Value;
        }

        // Only live descendants make a structure. The path's own entry (e.g. the tombstone of a removed field)
        // must not count, or a removed leaf would project as "{...}".
        return keys.Under(path).Any(key => overlay.TryGet(key, out _)) ? StructureValue : null;
    }

    private static bool IsStructure(object? value) => value is StructureValue;

    // Leaf data under a structural node, without the reserved $items / $type entries.
    private static bool IsLeafKey(string key) =>
        !key.EndsWith("/$items", StringComparison.Ordinal) && !key.EndsWith("/$type", StringComparison.Ordinal);

    private static RebaseConflictResponse ProjectConflict(ModelSchema schema, KVConflict conflict) => new(
        conflict.Path,
        schema.ToDisplayPath(conflict.Path),
        conflict.Kind.ToString(),
        conflict.Resolution.ToString(),
        conflict.BaseValue?.Value,
        conflict.MainValue?.Value,
        conflict.OursValue?.Value,
        conflict.IsIncoming,
        conflict.RequiresResolution);

    private static KVRebaseOutcome RebaseStateOutcome(KVOverlay overlay) =>
        overlay.HasUnresolvedConflicts ? KVRebaseOutcome.HasUnresolvedConflicts : KVRebaseOutcome.CanAutomerge;

    // A draft is current iff its base commit is the snapshot's head commit (the commit chain is the anchor).
    private static bool IsDraftCurrent(AggregateDraftDocument draft, KVSnapshot latestSnapshot) =>
        draft.BaseCommitId == latestSnapshot.LastCommitId;

    private static List<AggregateEventRecord> ToEventRecords(IReadOnlyList<object> events)
    {
        var now = DateTimeOffset.UtcNow;
        return events
            .Select(domainEvent => new AggregateEventRecord(
                domainEvent.GetType().Name,
                JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), EventJsonOptions),
                now))
            .ToList();
    }

    // ── Patch request mapping ─────────────────────────────────────────────────────

    private static KVPatchOperation ToPatchOperation(PatchOperationRequest request)
    {
        ApiRequestException.RequireText(request.OperationCode, "operationCode");
        ApiRequestException.RequireText(request.Path, "path");

        var operation = request.OperationCode.ToUpperInvariant();
        return operation switch
        {
            KVPatchOperations.Add => new KVPatchOperation(operation, request.Path, ToAddPayload(request.Value)),
            KVPatchOperations.Move => new KVPatchOperation(operation, request.Path, ToMovePayload(request.Value)),
            KVPatchOperations.Init => new KVPatchOperation(operation, request.Path, ToRequiredString(request.Value, operation)),
            KVPatchOperations.Unset or KVPatchOperations.Discard or KVPatchOperations.Remove or KVPatchOperations.Drop or KVPatchOperations.Reset
                => new KVPatchOperation(operation, request.Path),
            KVPatchOperations.Set => new KVPatchOperation(operation, request.Path, ToScalarValue(request.Value)),

            // A custom operation: its payload goes through as JSON and is converted to the method's argument type.
            _ => new KVPatchOperation(operation, request.Path, request.Value is { ValueKind: not JsonValueKind.Null } payload ? payload : null)
        };
    }

    private static KVAddPatchPayload ToAddPayload(JsonElement? value) =>
        value?.Deserialize<KVAddPatchPayload>(PatchJsonOptions)
        ?? throw new ApiRequestException("ADD patch operations require a KVAddPatchPayload value.");

    private static object ToMovePayload(JsonElement? value)
    {
        if (value is null)
        {
            throw new ApiRequestException("MOVE patch operations require a value.");
        }

        return value.Value.ValueKind == JsonValueKind.Number
            ? value.Value.GetInt32()
            : value.Value.Deserialize<KVMovePatchPayload>(PatchJsonOptions)!;
    }

    private static string ToRequiredString(JsonElement? value, string operation)
    {
        var result = value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
        return !string.IsNullOrWhiteSpace(result)
            ? result
            : throw new ApiRequestException($"{operation} patch operations require a non-empty string value.");
    }

    private static object? ToScalarValue(JsonElement? value) => value is null ? null : NormalizeValue(value.Value);

    private static KVValue? ToConflictValue(JsonElement? value)
    {
        var scalar = ToScalarValue(value);
        return scalar is null ? null : KVValue.FromObject(scalar);
    }

    private static object? NormalizeValue(JsonElement json) => json.ValueKind switch
    {
        JsonValueKind.String => json.GetString(),
        JsonValueKind.Number => json.TryGetDecimal(out var decimalValue) ? decimalValue : json.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,

        // Arrays (multi-select values) and objects go through as JSON; SET converts them to the field type.
        _ => json.Clone()
    };

    private static JsonSerializerOptions CreateEventJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    // ── Loading, persisting and projecting (through the session) ────────────────────

    private async Task<AggregateSnapshotDocument?> LoadSnapshotAsync(IAggregateModel model, Guid id, CancellationToken cancellationToken)
    {
        var document = await timings.MeasureAsync("load", () => session.LoadAsync<AggregateSnapshotDocument>(id, cancellationToken));
        if (document is null || !string.Equals(document.Model, model.Name, StringComparison.Ordinal))
        {
            return null;
        }

        // Migrate on load: an aggregate stored under an older schema is brought up to date the first time
        // it is read, and the migration commits join its history like any other commit.
        if (document.Snapshot.SchemaVersion < model.Definition.CurrentSchemaVersion)
        {
            var commits = timings.Measure("migrate", () => KVMigrator.Migrate(document.Snapshot, model.Definition));
            foreach (var commit in commits)
            {
                this.StoreMigrationCommit(model, document, commit);
            }

            session.Store(document);
            await timings.MeasureAsync("persist", () => session.SaveChangesAsync(cancellationToken));
        }
        else if (document.Snapshot.SchemaVersion > model.Definition.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Aggregate '{id}' is at schema version {document.Snapshot.SchemaVersion}, newer than this build ({model.Definition.CurrentSchemaVersion}).");
        }

        return document;
    }

    private async Task<KVSnapshot?> LoadParentSnapshotAsync(IAggregateModel model, AggregateSnapshotDocument document, CancellationToken cancellationToken)
    {
        if (document.ParentId is not { } parentId)
        {
            return null;
        }

        var parent = await this.LoadSnapshotAsync(model, parentId, cancellationToken)
                     ?? throw new InvalidOperationException($"Parent '{parentId}' of aggregate '{document.Id}' was not found.");
        return parent.Snapshot;
    }

    private async Task<(AggregateDraftDocument Draft, AggregateSnapshotDocument Snapshot, KVSnapshot? Parent)?> LoadDraftContextAsync(
        IAggregateModel model, Guid id, Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await timings.MeasureAsync("load", () => session.LoadAsync<AggregateDraftDocument>(draftId, cancellationToken));
        if (draft is null || draft.AggregateId != id || !string.Equals(draft.Model, model.Name, StringComparison.Ordinal))
        {
            return null;
        }

        var snapshotDocument = await this.LoadSnapshotAsync(model, id, cancellationToken);
        if (snapshotDocument is null)
        {
            return null;
        }

        var parentSnapshot = await this.LoadParentSnapshotAsync(model, snapshotDocument, cancellationToken);
        return (draft, snapshotDocument, parentSnapshot);
    }

    // The commits after the draft's base, in chain order, found by walking PreviousCommitId back from the
    // latest commit (timestamps can collide; the chain can't).
    // The commits from the draft's base (exclusive) to the latest, oldest first. Only the change sets stored
    // since the base's timestamp are read (with a second's margin for clock and storage precision), so a
    // draft one commit behind does not load the whole history; the chain walk then keeps exactly the
    // commits on the path. If that walk does not reach the base, the whole history is read instead.
    private async Task<IReadOnlyList<KVCommit>> LoadCommitsAfterAsync(
        IAggregateModel model, Guid id, Guid? baseCommitId, DateTimeOffset? baseTimestamp, Guid? latestCommitId, CancellationToken cancellationToken)
    {
        if (baseCommitId is not null && baseTimestamp is { } since)
        {
            var from = since.AddSeconds(-1);
            var recent = await timings.MeasureAsync("load", () => session.Query<AggregateChangeSetDocument>()
                .Where(document => document.AggregateId == id && document.Model == model.Name && document.Commit.Timestamp >= from)
                .ToListAsync(cancellationToken));
            if (Chain(recent, baseCommitId, latestCommitId, strict: true) is { } chain)
            {
                return chain;
            }
        }

        var documents = await timings.MeasureAsync("load", () => session.Query<AggregateChangeSetDocument>()
            .Where(document => document.AggregateId == id && document.Model == model.Name)
            .ToListAsync(cancellationToken));
        return Chain(documents, baseCommitId, latestCommitId, strict: false)!;
    }

    private void StoreMigrationCommit(IAggregateModel model, AggregateSnapshotDocument document, KVCommit commit)
    {
        session.Store(new AggregateChangeSetDocument
        {
            Id = commit.CommitId,
            Model = model.Name,
            AggregateId = document.Id,
            Commit = commit,
            Changes = [.. ProjectCommitChanges(model.Schema, null, commit)]
        });
    }

    private AggregateSnapshotResponse ProjectSnapshot(IAggregateModel model, AggregateSnapshotDocument document, KVSnapshot? parentSnapshot)
    {
        var overlay = KVOverlay.Create(document.Snapshot.Clone(), "system");
        var root = timings.Measure("bind", () => model.Bind(overlay, parentSnapshot));
        return timings.Measure("project", () => new AggregateSnapshotResponse(
            document.Id,
            model.Name,
            document.ParentId,
            AggregateDataProjector.Project(overlay, model.Definition),
            model.Computed(root),
            parentSnapshot is null ? [] : model.Definition.InheritedPrefixes,
            document.Snapshot.LastCommitId,
            document.Snapshot.LastCommitTimestamp,
            document.Snapshot.SchemaVersion));
    }

    private AggregateDraftResponse ProjectDraft(
        IAggregateModel model,
        AggregateDraftDocument draft,
        AggregateSnapshotDocument latest,
        KVSnapshot? parentSnapshot,
        KVRootNode? boundRoot = null)
    {
        var overlay = boundRoot?.Model.Overlay ?? draft.ToOverlay();
        var root = boundRoot ?? timings.Measure("bind", () => model.Bind(overlay, parentSnapshot));

        return timings.Measure("project", () =>
        {
            var deltas = root.GetAllChanges().Changes;
            return new AggregateDraftResponse(
                draft.Id,
                draft.AggregateId,
                model.Name,
                draft.User,
                latest.ParentId,
                AggregateDataProjector.Project(overlay, model.Definition),
                model.Computed(root),
                parentSnapshot is null ? [] : model.Definition.InheritedPrefixes,
                overlay.BaseCommitId,
                ProjectOverlayChanges(model.Schema, overlay, deltas),
                deltas.Select(delta => delta.Path).ToArray(),
                draft.IsRebasing,
                !draft.IsRebasing && !IsDraftCurrent(draft, latest.Snapshot),
                latest.Snapshot.LastCommitId,
                draft.IsRebasing ? draft.RebaseTarget?.LastCommitId : null,
                draft.Conflicts.Select(conflict => ProjectConflict(model.Schema, conflict)).ToArray(),
                draft.PendingEvents);
        });
    }

    /// <summary>Data keys sorted ordinally, so every key under a path is one contiguous range.</summary>
    private sealed class KeyIndex
    {
        private readonly string[] keys;

        public KeyIndex(IEnumerable<string> keys)
        {
            this.keys = keys.Distinct(StringComparer.Ordinal).ToArray();
            Array.Sort(this.keys, StringComparer.Ordinal);
        }

        public IEnumerable<string> Under(string path)
        {
            var prefix = path + "/";
            var index = Array.BinarySearch(this.keys, prefix, StringComparer.Ordinal);
            for (var i = index < 0 ? ~index : index; i < this.keys.Length && this.keys[i].StartsWith(prefix, StringComparison.Ordinal); i++)
            {
                yield return this.keys[i];
            }
        }
    }
}
