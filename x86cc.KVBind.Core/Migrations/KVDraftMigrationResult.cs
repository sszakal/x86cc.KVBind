using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

/// <summary>What happened to a draft in a draft migration.</summary>
public enum KVDraftMigrationOutcome
{
    /// <summary>The draft's base was already at the head's schema version; nothing changed.</summary>
    AlreadyCurrent = 0,

    /// <summary>The draft's changes were migrated and the draft now sits on the head.</summary>
    Rebased = 1,
}

/// <summary>One draft in a batch draft migration: a consumer-supplied identity, the draft and its aggregate's migrated head.</summary>
public sealed class KVDraftMigrationSubject
{
    public required object Key { get; init; }

    public required KVOverlay Draft { get; init; }

    /// <summary>The aggregate's latest snapshot, already migrated to the current schema version.</summary>
    public required KVSnapshot Head { get; init; }
}

/// <summary>The outcome of migrating one draft, with everything that did not survive it.</summary>
public sealed class KVDraftMigrationResult
{
    /// <summary>The subject's identity on the batch path; null for a single draft.</summary>
    public object? Key { get; init; }

    public required KVDraftMigrationOutcome Outcome { get; init; }

    /// <summary>The schema version of the draft's base before the migration.</summary>
    public required int FromVersion { get; init; }

    /// <summary>The schema version the draft is in now (the head's).</summary>
    public required int ToVersion { get; init; }

    /// <summary>
    /// The conflicts between the draft and the head (paths in the new layout), each resolved with the
    /// requested side. With <see cref="KVConflictResolution.Theirs"/> these are the draft's changes that were
    /// replaced: <see cref="KVConflict.OursValue"/> is what the draft had, <see cref="KVConflict.MainValue"/>
    /// what the head holds. Conflicts with a fixed outcome (<see cref="KVConflictKind.Order"/>: the draft's
    /// order of a collection lost) are included as they are, whatever <c>onConflict</c> says.
    /// </summary>
    public IReadOnlyList<KVConflict> ResolvedConflicts { get; init; } = [];

    /// <summary>
    /// The draft's own edits that a migration discarded: deleted (e.g. a removed field), or overwritten with
    /// the same value the committed data got, so nothing of the edit is left.
    /// </summary>
    public IReadOnlyList<KVDraftChangeRemoval> RemovedByMigration { get; init; } = [];

    /// <summary>True when a rebase in progress (started in the old layout) was abandoned, with its review choices.</summary>
    public bool AbandonedRebase { get; init; }
}

/// <summary>A draft edit a migration discarded: its path and the draft's value, in the layout it was discarded from.</summary>
public sealed class KVDraftChangeRemoval
{
    public required string Path { get; init; }

    public required KVValue Value { get; init; }

    /// <summary>The migration (target schema version) that discarded it.</summary>
    public required int Version { get; init; }
}
