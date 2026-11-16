using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

/// <summary>
/// Turns pending migrations into migration commits. Each migration whose <see cref="KVMigration.ToVersion"/>
/// is newer than the snapshot's <see cref="KVSnapshot.SchemaVersion"/> produces one commit, in version order,
/// each chained off the previous (so a later migration sees the earlier one's result).
/// </summary>
public static partial class KVMigrator
{
    public const string MigrationUser = "system:migration";

    // One participant in a migration run: data that advances migration by migration under one context.
    private interface IMigrationRun
    {
        object? Key { get; }

        // The schema version reached so far.
        int Version { get; }

        // The newest schema version this run may reach.
        int TargetVersion { get; }

        // What the migration's prepare phase is shown for this run.
        KVMigrationSubject PrepareSubject { get; }

        void Apply(KVMigration migration, KVMigrationContext context);
    }

    /// <summary>
    /// Builds the ordered migration commits needed to bring <paramref name="snapshot"/> up to the newest
    /// registered migration. The snapshot is not mutated — apply the returned commits to advance it. A
    /// migration that produces no data change still yields a commit (empty <see cref="KVCommit.Changes"/>)
    /// so the version advances.
    /// </summary>
    public static IReadOnlyList<KVCommit> BuildCommits(
        KVSnapshot snapshot,
        IEnumerable<KVMigration> migrations,
        string user = MigrationUser,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(migrations);

        // No definition here → no knowledge of inherited members, so nothing is stripped.
        return BuildCommitsCore(snapshot, Sorted(migrations), inheritedPrefixes: [], user, timestamp);
    }

    /// <summary>
    /// Builds the pending migration commits using the migrations registered on a root definition. Takes an
    /// O(1) fast path when the snapshot is already current (the migration list is not scanned), and otherwise
    /// binary-searches to the pending tail — so already-applied migrations add no runtime cost as they grow.
    /// </summary>
    public static IReadOnlyList<KVCommit> BuildCommits(
        KVSnapshot snapshot,
        KVNodeDefinition definition,
        string user = MigrationUser,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(definition);

        // A child snapshot must not migrate its inherited members (always read from the parent).
        return BuildCommitsCore(snapshot, definition.MigrationsByVersion, definition.InheritedPrefixes, user, timestamp);
    }

    /// <summary>Builds the migration commits and applies them to <paramref name="snapshot"/> in place.</summary>
    public static IReadOnlyList<KVCommit> Migrate(
        KVSnapshot snapshot,
        IEnumerable<KVMigration> migrations,
        string user = MigrationUser,
        DateTimeOffset? timestamp = null)
    {
        var commits = BuildCommits(snapshot, migrations, user, timestamp);
        foreach (var commit in commits)
            snapshot.Apply(commit);
        return commits;
    }

    /// <summary>Applies the pending migrations registered on a root definition to <paramref name="snapshot"/>.</summary>
    public static IReadOnlyList<KVCommit> Migrate(
        KVSnapshot snapshot,
        KVNodeDefinition definition,
        string user = MigrationUser,
        DateTimeOffset? timestamp = null)
    {
        var commits = BuildCommits(snapshot, definition, user, timestamp);
        foreach (var commit in commits)
            snapshot.Apply(commit);
        return commits;
    }

    /// <summary>
    /// Migrates a batch of aggregates, each potentially at a different version. For every migration, the async
    /// prepare phase (if any) runs once over just the subset that still needs it — so external data is fetched
    /// in one round-trip per migration, not per aggregate. Each subject's snapshot is advanced in place and its
    /// commits returned for persistence; subjects already current contribute no commits and trigger no prepare.
    /// </summary>
    public static Task<IReadOnlyList<KVMigrationResult>> MigrateBatchAsync(
        IReadOnlyList<KVMigrationSubject> subjects,
        IEnumerable<KVMigration> migrations,
        string user = MigrationUser,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(migrations);

        // No definition here → nothing is stripped.
        return MigrateBatchCoreAsync(subjects, Sorted(migrations), inheritedPrefixes: [], user, timestamp, cancellationToken);
    }

    /// <summary>
    /// Batch-migrates using the migrations registered on a root definition. Skips migrations already applied
    /// across the whole batch by slicing from the lowest version present, so a batch that is entirely current
    /// does no per-migration work.
    /// </summary>
    public static Task<IReadOnlyList<KVMigrationResult>> MigrateBatchAsync(
        IReadOnlyList<KVMigrationSubject> subjects,
        KVNodeDefinition definition,
        string user = MigrationUser,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(definition);

        return MigrateBatchCoreAsync(subjects, definition.MigrationsByVersion, definition.InheritedPrefixes, user, timestamp, cancellationToken);
    }

    // True when a snapshot carries the child mark (its inherited members are owned by the parent).
    private static bool IsChild(KVSnapshot snapshot) => snapshot.Data.ContainsKey(KVOverlay.ChildMarkKey);

    // Drops every delta key under an inherited prefix — a child must never migrate (e.g. backfill) the
    // fields it always reads from its parent. No-op when the list is empty (master, or definition-less call).
    private static void StripInheritedPaths(KVDictionary delta, IReadOnlyList<string> inheritedPrefixes)
    {
        if (inheritedPrefixes.Count == 0 || delta.Count == 0)
            return;

        foreach (var key in delta.Keys.ToList())
        {
            foreach (var prefix in inheritedPrefixes)
            {
                if (KVPath.IsSameOrDescendant(key, prefix))
                {
                    delta.Remove(key);
                    break;
                }
            }
        }
    }

    // One snapshot's migration commits, built against a working copy so the snapshot itself is untouched.
    // An already-current snapshot is not even copied.
    private static IReadOnlyList<KVCommit> BuildCommitsCore(
        KVSnapshot snapshot,
        KVMigration[] sorted,
        IReadOnlyList<string> inheritedPrefixes,
        string user,
        DateTimeOffset? timestamp)
    {
        if (sorted.Length == 0 || snapshot.SchemaVersion >= sorted[^1].ToVersion)
            return Array.Empty<KVCommit>();

        var run = new SnapshotMigrationRun(snapshot.Clone(), subject: null, inheritedPrefixes, user, timestamp ?? DateTimeOffset.UtcNow);
        RunMigrations([run], sorted);
        return run.Commits;
    }

    private static async Task<IReadOnlyList<KVMigrationResult>> MigrateBatchCoreAsync(
        IReadOnlyList<KVMigrationSubject> subjects,
        KVMigration[] sorted,
        IReadOnlyList<string> inheritedPrefixes,
        string user,
        DateTimeOffset? timestamp,
        CancellationToken cancellationToken)
    {
        var stamp = timestamp ?? DateTimeOffset.UtcNow;
        var runs = subjects
            .Select(subject => new SnapshotMigrationRun(subject.Snapshot, subject, inheritedPrefixes, user, stamp))
            .ToList();

        await RunMigrationsAsync(runs, sorted, cancellationToken).ConfigureAwait(false);

        return runs
            .Select(run => new KVMigrationResult { Key = run.Subject!.Key, Commits = run.Commits })
            .ToList();
    }

    // ── The migration loop, shared by snapshots and drafts ────────────────────────────────────────────

    private static KVMigration[] Sorted(IEnumerable<KVMigration> migrations)
        => migrations.OrderBy(migration => migration.ToVersion).ToArray();

    // Runs every pending migration over the runs, in version order, without prepare phases.
    private static void RunMigrations<TRun>(IReadOnlyList<TRun> runs, KVMigration[] sorted)
        where TRun : IMigrationRun
    {
        foreach (var (migration, subset) in PendingMigrations(runs, sorted))
        {
            foreach (var run in subset)
                run.Apply(migration, KVMigrationContext.None);
        }
    }

    // Runs every pending migration over the runs, in version order. A migration's prepare phase runs once
    // over just the runs that still need it, and they share what it returns.
    private static async Task RunMigrationsAsync<TRun>(IReadOnlyList<TRun> runs, KVMigration[] sorted, CancellationToken cancellationToken)
        where TRun : IMigrationRun
    {
        foreach (var (migration, subset) in PendingMigrations(runs, sorted))
        {
            var prepared = migration.Prepare is null
                ? null
                : await migration.Prepare(subset.Select(run => run.PrepareSubject).ToList(), cancellationToken).ConfigureAwait(false);

            foreach (var run in subset)
                run.Apply(migration, new KVMigrationContext(run.Key, prepared));
        }
    }

    // Each migration that some run still needs, with those runs. Starts at the first migration newer than the
    // lowest version in the batch (binary search), so applied migrations cost nothing however many there are,
    // and a batch that is entirely current does no per-migration work. Lazy: a run's version is read after
    // the previous migration was applied to it.
    private static IEnumerable<(KVMigration Migration, List<TRun> Subset)> PendingMigrations<TRun>(IReadOnlyList<TRun> runs, KVMigration[] sorted)
        where TRun : IMigrationRun
    {
        if (runs.Count == 0 || sorted.Length == 0)
            yield break;

        var lowest = runs.Min(run => run.Version);
        if (lowest >= sorted[^1].ToVersion)
            yield break;

        for (var index = FirstVersionAbove(sorted, lowest); index < sorted.Length; index++)
        {
            var migration = sorted[index];
            var subset = runs.Where(run => migration.ToVersion > run.Version && migration.ToVersion <= run.TargetVersion).ToList();
            if (subset.Count > 0)
                yield return (migration, subset);
        }
    }

    // One migration's delta over the given data: every step reads the same (pre-migration) source. For a
    // child, writes under an inherited prefix are dropped.
    private static KVDictionary BuildDelta(
        KVSnapshot working,
        KVMigration migration,
        KVMigrationContext context,
        IReadOnlyList<string> inheritedPrefixes)
    {
        var delta = new KVDictionary();
        foreach (var step in migration.Steps)
            step.BuildInto(working.Data, delta, context);

        StripInheritedPaths(delta, IsChild(working) ? inheritedPrefixes : []);
        return delta;
    }

    // The migration commit continuing the working snapshot's chain.
    private static KVCommit CreateCommit(KVSnapshot working, KVMigration migration, KVDictionary delta, string user, DateTimeOffset stamp)
    {
        return new KVCommit
        {
            CommitId = Guid.NewGuid(),
            PreviousCommitId = working.LastCommitId,
            User = user,
            Timestamp = stamp,
            MigrationToVersion = migration.ToVersion,
            Changes = delta
        };
    }

    // First index in a version-sorted array whose ToVersion is strictly greater than version (upper bound).
    private static int FirstVersionAbove(KVMigration[] sorted, int version)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (sorted[mid].ToVersion > version)
                hi = mid;
            else
                lo = mid + 1;
        }

        return lo;
    }

    // A snapshot advanced in place: each migration becomes a commit chained off the previous one, applied so
    // the next migration builds against its output.
    private sealed class SnapshotMigrationRun(
        KVSnapshot working,
        KVMigrationSubject? subject,
        IReadOnlyList<string> inheritedPrefixes,
        string user,
        DateTimeOffset stamp) : IMigrationRun
    {
        public KVMigrationSubject? Subject => subject;

        public List<KVCommit> Commits { get; } = [];

        public object? Key => subject?.Key;

        public int Version => working.SchemaVersion;

        public int TargetVersion => int.MaxValue;

        public KVMigrationSubject PrepareSubject
            => subject ?? throw new InvalidOperationException("Prepare phases only run on the batch path.");

        public void Apply(KVMigration migration, KVMigrationContext context)
        {
            var delta = BuildDelta(working, migration, context, inheritedPrefixes);
            var commit = CreateCommit(working, migration, delta, user, stamp);
            working.Apply(commit);
            this.Commits.Add(commit);
        }
    }
}
