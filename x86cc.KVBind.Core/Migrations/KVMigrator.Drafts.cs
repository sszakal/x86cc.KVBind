using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

public static partial class KVMigrator
{
    /// <summary>
    /// Moves a draft that is behind the schema onto <paramref name="head"/>, the aggregate's latest snapshot,
    /// which must already be migrated to the definition's current schema version. The draft's base stays
    /// immutable: the base and the draft's view (base plus changes) are migrated as throwaway copies, the
    /// draft's changes are re-derived in the new layout, and they are merged with everything the head holds
    /// that the base did not (other users' commits and the migration commits alike). Every conflict is
    /// resolved with <paramref name="onConflict"/>, so the rebase completes in one call and the draft ends up
    /// based on <paramref name="head"/>. A draft whose base is already at the head's schema version is left
    /// alone (<see cref="KVDraftMigrationOutcome.AlreadyCurrent"/>); catching up on ordinary commits is the
    /// normal, interactive rebase. A rebase in progress is abandoned and redone. Change reactions do not run:
    /// call <c>root.NotifyReplaced()</c> after binding the draft. Prepare phases only run on the batch path
    /// (<see cref="RebaseDraftsAsync(IReadOnlyList{KVDraftMigrationSubject}, KVNodeDefinition, KVConflictResolution, CancellationToken)"/>).
    /// </summary>
    public static KVDraftMigrationResult RebaseDraft(
        KVOverlay draft,
        KVSnapshot head,
        KVNodeDefinition definition,
        KVConflictResolution onConflict = KVConflictResolution.Theirs)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(definition);
        RequireCurrentHead(head, definition);

        var run = DraftMigrationRun.Start(draft, head, definition.InheritedPrefixes, onConflict, key: null);
        RunMigrations([run], definition.MigrationsByVersion);
        return run.Finish();
    }

    /// <summary>
    /// Moves a draft that is behind the schema onto <paramref name="head"/> using an explicit migration list
    /// (migrations newer than the draft's base, up to the head's schema version). No definition here, so no
    /// inherited members are known and nothing is stripped. See
    /// <see cref="RebaseDraft(KVOverlay, KVSnapshot, KVNodeDefinition, KVConflictResolution)"/>.
    /// </summary>
    public static KVDraftMigrationResult RebaseDraft(
        KVOverlay draft,
        KVSnapshot head,
        IEnumerable<KVMigration> migrations,
        KVConflictResolution onConflict = KVConflictResolution.Theirs)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(migrations);

        var run = DraftMigrationRun.Start(draft, head, inheritedPrefixes: [], onConflict, key: null);
        RunMigrations([run], Sorted(migrations));
        return run.Finish();
    }

    /// <summary>
    /// Moves a batch of drafts onto their aggregates' (already migrated) heads, as
    /// <see cref="RebaseDraft(KVOverlay, KVSnapshot, KVNodeDefinition, KVConflictResolution)"/> does for one.
    /// Each migration's prepare phase runs once over the drafts that still need it; it receives each draft's
    /// view (base plus changes) as the subject's snapshot, and the base and the draft of one subject share
    /// the prepared data. Results are returned in subject order.
    /// </summary>
    public static Task<IReadOnlyList<KVDraftMigrationResult>> RebaseDraftsAsync(
        IReadOnlyList<KVDraftMigrationSubject> subjects,
        KVNodeDefinition definition,
        KVConflictResolution onConflict = KVConflictResolution.Theirs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(definition);
        foreach (var subject in subjects)
            RequireCurrentHead(subject.Head, definition);

        var runs = subjects
            .Select(subject => DraftMigrationRun.Start(subject.Draft, subject.Head, definition.InheritedPrefixes, onConflict, subject.Key))
            .ToList();
        return RebaseDraftsCoreAsync(runs, definition.MigrationsByVersion, cancellationToken);
    }

    /// <summary>
    /// Batch variant of <see cref="RebaseDraft(KVOverlay, KVSnapshot, IEnumerable{KVMigration}, KVConflictResolution)"/>
    /// over an explicit migration list.
    /// </summary>
    public static Task<IReadOnlyList<KVDraftMigrationResult>> RebaseDraftsAsync(
        IReadOnlyList<KVDraftMigrationSubject> subjects,
        IEnumerable<KVMigration> migrations,
        KVConflictResolution onConflict = KVConflictResolution.Theirs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(migrations);

        var runs = subjects
            .Select(subject => DraftMigrationRun.Start(subject.Draft, subject.Head, inheritedPrefixes: [], onConflict, subject.Key))
            .ToList();
        return RebaseDraftsCoreAsync(runs, Sorted(migrations), cancellationToken);
    }

    /// <summary>
    /// The difference between two flat data sets as a change-set over <paramref name="from"/>: an upsert for
    /// every key that is new or changed, and a tombstone for every removed key, raised to the top-most
    /// collection item or nested node that is gone entirely (so a removed item is one tombstone at its path).
    /// </summary>
    internal static KVDictionary Diff(KVDictionary from, KVDictionary to)
    {
        var diff = new KVDictionary();
        foreach (var (key, value) in to)
        {
            if (!from.TryGetValue(key, out var previous) || !KVMerge.ValueEquals(previous, value))
                diff[key] = value;
        }

        HashSet<string>? live = null;
        foreach (var key in from.Keys)
        {
            if (to.ContainsKey(key))
                continue;

            live ??= LivePaths(to);
            diff[TopMostRemoved(from, key, live)] = KVValue.Tombstone;
        }

        return diff;
    }

    // Every key of the data plus all of its ancestors: the paths that still hold something.
    private static HashSet<string> LivePaths(KVDictionary data)
    {
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in data.Keys)
        {
            live.Add(key);
            for (var slash = key.IndexOf('/'); slash >= 0; slash = key.IndexOf('/', slash + 1))
                live.Add(key[..slash]);
        }

        return live;
    }

    // The removed key itself, or its top-most ancestor object that is gone entirely: a collection item (its
    // parent lists members in $items) or a nested node (it holds a $type). Field groups and other plain
    // ancestors are never raised to, so a conflict stays on the field that was removed.
    private static string TopMostRemoved(KVDictionary from, string key, HashSet<string> live)
    {
        for (var slash = key.IndexOf('/'); slash >= 0; slash = key.IndexOf('/', slash + 1))
        {
            var ancestor = key[..slash];
            if (!live.Contains(ancestor) && IsObject(from, ancestor))
                return ancestor;
        }

        return key;
    }

    private static bool IsObject(KVDictionary data, string path)
    {
        if (data.ContainsKey(path + "/$type"))
            return true;

        var slash = path.LastIndexOf('/');
        return slash > 0 && data.ContainsKey(path[..slash] + "/$items");
    }

    private static bool HasTombstonedAncestor(KVDictionary changes, string key)
    {
        for (var slash = key.LastIndexOf('/'); slash > 0; slash = key.LastIndexOf('/', slash - 1))
        {
            if (changes.TryGetValue(key[..slash], out var value) && value == KVValue.Tombstone)
                return true;
        }

        return false;
    }

    private static void RequireCurrentHead(KVSnapshot head, KVNodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (head.SchemaVersion != definition.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The head snapshot is at schema version {head.SchemaVersion}, not the current {definition.CurrentSchemaVersion}. Migrate the snapshot first.");
        }
    }

    private static async Task<IReadOnlyList<KVDraftMigrationResult>> RebaseDraftsCoreAsync(
        IReadOnlyList<DraftMigrationRun> runs,
        KVMigration[] sorted,
        CancellationToken cancellationToken)
    {
        await RunMigrationsAsync(runs, sorted, cancellationToken).ConfigureAwait(false);
        return runs.Select(run => run.Finish()).ToList();
    }

    // One draft's migration: the base and the draft's view advance migration by migration (sharing each
    // migration's context), then the draft's changes are re-derived and merged onto the head.
    private sealed class DraftMigrationRun : IMigrationRun
    {
        private readonly KVOverlay draft;
        private readonly KVSnapshot head;
        private readonly IReadOnlyList<string> inheritedPrefixes;
        private readonly KVConflictResolution onConflict;
        private readonly bool abandonedRebase;
        private readonly int fromVersion;

        // Leaves the draft itself wrote (its value differs from the base), tracked through renames by their
        // value instance, so an edit a migration removes can be reported.
        private readonly Dictionary<string, KVValue> written = new(StringComparer.Ordinal);
        private readonly List<KVDraftChangeRemoval> removed = [];

        private readonly KVSnapshot? baseWork;

        private KVMigrationSubject? prepareSubject;

        private DraftMigrationRun(
            KVOverlay draft,
            KVSnapshot head,
            IReadOnlyList<string> inheritedPrefixes,
            KVConflictResolution onConflict,
            object? key,
            bool abandonedRebase)
        {
            this.draft = draft;
            this.head = head;
            this.inheritedPrefixes = inheritedPrefixes;
            this.onConflict = onConflict;
            this.Key = key;
            this.abandonedRebase = abandonedRebase;
            this.fromVersion = draft.Snapshot.SchemaVersion;

            if (this.IsCurrent)
                return;

            // Throwaway copies: the stored base is never touched.
            this.baseWork = draft.Snapshot.Clone();
            this.DraftView = draft.Snapshot.Clone();
            this.DraftView.Data = Materialize(draft);

            foreach (var (path, value) in draft.Changes)
            {
                if (value != KVValue.Tombstone
                    && this.DraftView.Data.TryGetValue(path, out var visible)
                    && !(draft.Snapshot.Data.TryGetValue(path, out var original) && KVMerge.ValueEquals(original, visible)))
                {
                    this.written[path] = visible;
                }
            }
        }

        public object? Key { get; }

        // The draft as if it were committed (base plus changes), in the layout of the last applied migration.
        public KVSnapshot DraftView { get; } = null!;

        public int Version => this.IsCurrent ? this.fromVersion : this.DraftView.SchemaVersion;

        public int TargetVersion => this.head.SchemaVersion;

        // The prepare phase is shown the draft's view, keyed like the draft.
        public KVMigrationSubject PrepareSubject
            => this.prepareSubject ??= new KVMigrationSubject { Key = this.Key!, Snapshot = this.DraftView };

        private bool IsCurrent => this.fromVersion == this.head.SchemaVersion;

        public static DraftMigrationRun Start(
            KVOverlay draft,
            KVSnapshot head,
            IReadOnlyList<string> inheritedPrefixes,
            KVConflictResolution onConflict,
            object? key)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(head);
            if (onConflict is not (KVConflictResolution.Ours or KVConflictResolution.Theirs))
                throw new ArgumentOutOfRangeException(nameof(onConflict), onConflict, "Conflicts can only be resolved as Ours or Theirs.");
            if (draft.Snapshot.SchemaVersion > head.SchemaVersion)
            {
                throw new InvalidOperationException(
                    $"The draft's base is at schema version {draft.Snapshot.SchemaVersion}, newer than the head's {head.SchemaVersion}.");
            }

            // A rebase in progress was started in the old layout; its review list no longer applies.
            var abandoned = false;
            if (draft.IsRebasing && draft.Snapshot.SchemaVersion < head.SchemaVersion)
            {
                draft.CancelRebase();
                abandoned = true;
            }

            return new DraftMigrationRun(draft, head, inheritedPrefixes, onConflict, key, abandoned);
        }

        public void Apply(KVMigration migration, KVMigrationContext context)
        {
            var migratedBase = this.baseWork!; // set whenever a migration is pending
            var baseDelta = BuildDelta(migratedBase, migration, context, this.inheritedPrefixes);
            var draftDelta = BuildDelta(this.DraftView, migration, context, this.inheritedPrefixes);
            this.TrackWritten(draftDelta, migratedBase, baseDelta, migration.ToVersion);

            migratedBase.Apply(CreateCommit(migratedBase, migration, baseDelta, MigrationUser, DateTimeOffset.UtcNow));
            this.DraftView.Apply(CreateCommit(this.DraftView, migration, draftDelta, MigrationUser, DateTimeOffset.UtcNow));
        }

        public KVDraftMigrationResult Finish()
        {
            if (this.IsCurrent)
            {
                return new KVDraftMigrationResult
                {
                    Key = this.Key,
                    Outcome = KVDraftMigrationOutcome.AlreadyCurrent,
                    FromVersion = this.fromVersion,
                    ToVersion = this.fromVersion
                };
            }

            // Ours and theirs, both over the migrated base, both in the head's layout.
            var mine = Diff(this.baseWork!.Data, this.DraftView.Data);
            var theirs = Diff(this.baseWork.Data, this.head.Data);
            var resolved = new List<KVConflict>();

            KVDictionary changes;
            if (theirs.Count == 0)
            {
                changes = mine;
            }
            else
            {
                var merge = KVOverlay.Create(this.baseWork, this.draft.User);
                merge.Changes = mine;
                var upstream = new KVCommit { PreviousCommitId = this.baseWork.LastCommitId, Changes = theirs };
                merge.BeginRebase(this.head, [upstream]);

                foreach (var conflict in merge.Conflicts.ToList())
                {
                    if (conflict.RequiresResolution)
                        merge.ResolveConflict(conflict.Path, this.onConflict);
                    if (conflict.RequiresResolution || conflict.IsFixed)
                        resolved.Add(conflict); // fixed ones (a lost order) are reported as they are
                }

                merge.FinishRebase();
                changes = merge.Changes;
            }

            this.draft.Reset(this.head);
            this.draft.Changes = changes;

            return new KVDraftMigrationResult
            {
                Key = this.Key,
                Outcome = KVDraftMigrationOutcome.Rebased,
                FromVersion = this.fromVersion,
                ToVersion = this.head.SchemaVersion,
                ResolvedConflicts = resolved,
                RemovedByMigration = this.removed,
                AbandonedRebase = this.abandonedRebase
            };
        }

        // The draft's effective data (base plus changes, without any inheritance layer).
        private static KVDictionary Materialize(KVOverlay draft)
        {
            var view = KVOverlay.Create(draft.Snapshot, draft.User);
            view.Changes = draft.Changes;

            var data = new KVDictionary();
            foreach (var key in draft.Snapshot.Data.Keys)
            {
                if (view.TryGet(key, out var value) && value is not null)
                    data[key] = value;
            }

            foreach (var (key, change) in draft.Changes)
            {
                if (change != KVValue.Tombstone && view.TryGet(key, out var value) && value is not null)
                    data[key] = value;
            }

            return data;
        }

        private static bool IsRemovedBy(KVDictionary delta, string path)
        {
            if (delta.TryGetValue(path, out var exact) && exact == KVValue.Tombstone)
                return true;

            return HasTombstonedAncestor(delta, path);
        }

        private static Dictionary<KVValue, Queue<string>> MovedValues(KVDictionary delta)
        {
            var moves = new Dictionary<KVValue, Queue<string>>(ReferenceEqualityComparer.Instance);
            foreach (var (path, value) in delta)
            {
                if (value == KVValue.Tombstone)
                    continue;
                if (!moves.TryGetValue(value, out var targets))
                    moves[value] = targets = new Queue<string>();
                targets.Enqueue(path);
            }

            return moves;
        }

        // The value a path holds in the base once the base's delta is applied (null when absent).
        private static KVValue? BaseValueAfter(KVSnapshot migratedBase, KVDictionary baseDelta, string path)
        {
            if (baseDelta.TryGetValue(path, out var written))
                return written == KVValue.Tombstone ? null : written;
            if (IsRemovedBy(baseDelta, path))
                return null;
            return migratedBase.Data.TryGetValue(path, out var value) ? value : null;
        }

        // Follows the draft's own edits through one migration delta: an edit whose value moved (a rename
        // carries the same value instance) is tracked at its new path. One that is deleted without moving,
        // or overwritten with exactly what the base gets there (so nothing of the draft's edit is left), was
        // removed by the migration.
        private void TrackWritten(KVDictionary delta, KVSnapshot migratedBase, KVDictionary baseDelta, int version)
        {
            if (this.written.Count == 0 || delta.Count == 0)
                return;

            Dictionary<KVValue, Queue<string>>? moves = null;
            foreach (var (path, value) in this.written.ToList())
            {
                if (delta.TryGetValue(path, out var replacement) && replacement != KVValue.Tombstone)
                {
                    if (KVMerge.ValueEquals(BaseValueAfter(migratedBase, baseDelta, path), replacement))
                    {
                        this.written.Remove(path);
                        this.removed.Add(new KVDraftChangeRemoval { Path = path, Value = value, Version = version });
                    }
                    else
                    {
                        this.written[path] = replacement; // rewritten in place, still the draft's own (e.g. derived from it)
                    }

                    continue;
                }

                if (!IsRemovedBy(delta, path))
                    continue;

                this.written.Remove(path);
                moves ??= MovedValues(delta);
                if (moves.TryGetValue(value, out var targets) && targets.Count > 0)
                    this.written[targets.Dequeue()] = value;
                else
                    this.removed.Add(new KVDraftChangeRemoval { Path = path, Value = value, Version = version });
            }
        }
    }
}
