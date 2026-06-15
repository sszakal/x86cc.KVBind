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
public static class KVMigrator
{
    public const string MigrationUser = "system:migration";

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

        var pending = migrations
            .Where(migration => migration.ToVersion > snapshot.SchemaVersion)
            .OrderBy(migration => migration.ToVersion)
            .ToList();

        // No definition here → no knowledge of inherited members, so nothing is stripped.
        return BuildFromPending(snapshot, pending, user, timestamp, inheritedPrefixes: []);
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
            foreach (var prefix in inheritedPrefixes)
                if (KVPath.IsSameOrDescendant(key, prefix))
                {
                    delta.Remove(key);
                    break;
                }
    }

    // Builds commits from an already-ordered pending list. Each commit chains off the previous so a later
    // migration sees the earlier one's output. For a child snapshot, inherited-path writes are stripped.
    private static IReadOnlyList<KVCommit> BuildFromPending(
        KVSnapshot snapshot,
        IReadOnlyList<KVMigration> pending,
        string user,
        DateTimeOffset? timestamp,
        IReadOnlyList<string> inheritedPrefixes)
    {
        if (pending.Count == 0)
            return Array.Empty<KVCommit>();

        var stamp = timestamp ?? DateTimeOffset.UtcNow;
        var working = snapshot.Clone();
        var commits = new List<KVCommit>(pending.Count);
        var stripPrefixes = IsChild(snapshot) ? inheritedPrefixes : [];

        foreach (var migration in pending)
        {
            var delta = new KVDictionary();
            foreach (var step in migration.Steps)
                step.BuildInto(working.Data, delta, KVMigrationContext.None);

            StripInheritedPaths(delta, stripPrefixes);

            var commit = new KVCommit
            {
                CommitId = Guid.NewGuid(),
                PreviousCommitId = working.LastCommitId,
                User = user,
                Timestamp = stamp,
                MigrationToVersion = migration.ToVersion,
                Changes = delta
            };

            working.Apply(commit); // so the next migration builds against this one's output
            commits.Add(commit);
        }

        return commits;
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

        if (snapshot.SchemaVersion >= definition.CurrentSchemaVersion)
            return Array.Empty<KVCommit>();

        var sorted = definition.MigrationsByVersion;
        var start = FirstVersionAbove(sorted, snapshot.SchemaVersion);
        var pending = new ArraySegment<KVMigration>(sorted, start, sorted.Length - start);
        // A child snapshot must not migrate its inherited members (always read from the parent).
        return BuildFromPending(snapshot, pending, user, timestamp, definition.InheritedPrefixes);
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
        return MigrateBatchCoreAsync(subjects, migrations, inheritedPrefixes: [], user, timestamp, cancellationToken);
    }

    private static async Task<IReadOnlyList<KVMigrationResult>> MigrateBatchCoreAsync(
        IReadOnlyList<KVMigrationSubject> subjects,
        IEnumerable<KVMigration> migrations,
        IReadOnlyList<string> inheritedPrefixes,
        string user,
        DateTimeOffset? timestamp,
        CancellationToken cancellationToken)
    {
        var ordered = migrations.OrderBy(migration => migration.ToVersion).ToList();
        var stamp = timestamp ?? DateTimeOffset.UtcNow;
        var commitsByKey = subjects.ToDictionary(subject => subject.Key, _ => new List<KVCommit>());

        foreach (var migration in ordered)
        {
            // Only aggregates still behind this migration take part — mixed-version and already-current
            // subjects fall out naturally, so a fully up-to-date batch does nothing.
            var subset = subjects.Where(subject => subject.Snapshot.SchemaVersion < migration.ToVersion).ToList();
            if (subset.Count == 0)
                continue;

            var prepared = migration.Prepare is null
                ? null
                : await migration.Prepare(subset, cancellationToken).ConfigureAwait(false);

            foreach (var subject in subset)
            {
                var context = new KVMigrationContext(subject.Key, prepared);
                var delta = new KVDictionary();
                foreach (var step in migration.Steps)
                    step.BuildInto(subject.Snapshot.Data, delta, context);

                // Per subject: a child must not migrate its inherited members.
                StripInheritedPaths(delta, IsChild(subject.Snapshot) ? inheritedPrefixes : []);

                var commit = new KVCommit
                {
                    CommitId = Guid.NewGuid(),
                    PreviousCommitId = subject.Snapshot.LastCommitId,
                    User = user,
                    Timestamp = stamp,
                    MigrationToVersion = migration.ToVersion,
                    Changes = delta
                };

                subject.Snapshot.Apply(commit);
                commitsByKey[subject.Key].Add(commit);
            }
        }

        return subjects
            .Select(subject => new KVMigrationResult { Key = subject.Key, Commits = commitsByKey[subject.Key] })
            .ToList();
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

        var sorted = definition.MigrationsByVersion;
        if (subjects.Count == 0 || sorted.Length == 0)
            return Task.FromResult<IReadOnlyList<KVMigrationResult>>(
                subjects.Select(subject => new KVMigrationResult { Key = subject.Key, Commits = Array.Empty<KVCommit>() }).ToList());

        var minVersion = int.MaxValue;
        foreach (var subject in subjects)
            if (subject.Snapshot.SchemaVersion < minVersion)
                minVersion = subject.Snapshot.SchemaVersion;

        // The whole batch is already current — nothing applies, skip without touching any migration.
        if (minVersion >= definition.CurrentSchemaVersion)
            return Task.FromResult<IReadOnlyList<KVMigrationResult>>(
                subjects.Select(subject => new KVMigrationResult { Key = subject.Key, Commits = Array.Empty<KVCommit>() }).ToList());

        var start = FirstVersionAbove(sorted, minVersion);
        var slice = new ArraySegment<KVMigration>(sorted, start, sorted.Length - start);
        return MigrateBatchCoreAsync(subjects, slice, definition.InheritedPrefixes, user, timestamp, cancellationToken);
    }
}
