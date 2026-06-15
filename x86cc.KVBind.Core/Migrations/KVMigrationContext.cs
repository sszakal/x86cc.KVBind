using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

/// <summary>
/// One aggregate in a batch migration: a consumer-supplied identity paired with its snapshot. The identity
/// lets an async prepare phase key the data it fetches (KVBind snapshots carry no id of their own).
/// </summary>
public sealed class KVMigrationSubject
{
    public required object Key { get; init; }

    public required KVSnapshot Snapshot { get; init; }
}

/// <summary>
/// Per-aggregate context handed to backfill factories during a build: the current subject's identity and
/// whatever its migration's prepare phase produced for the batch. Empty (both null) on the synchronous path.
/// </summary>
public sealed class KVMigrationContext
{
    internal static readonly KVMigrationContext None = new(key: null, prepared: null);

    internal KVMigrationContext(object? key, object? prepared)
    {
        Key = key;
        Prepared = prepared;
    }

    /// <summary>The current aggregate's identity, or null on the synchronous path.</summary>
    public object? Key { get; }

    /// <summary>Whatever this migration's prepare phase returned for the batch (e.g. a lookup keyed by id).</summary>
    public object? Prepared { get; }

    public TKey KeyAs<TKey>() => (TKey)Key!;

    public TPrepared PreparedAs<TPrepared>() => (TPrepared)Prepared!;
}

/// <summary>The migration commits produced for one aggregate in a batch run, in chain order.</summary>
public sealed class KVMigrationResult
{
    public required object Key { get; init; }

    public required IReadOnlyList<KVCommit> Commits { get; init; }
}
