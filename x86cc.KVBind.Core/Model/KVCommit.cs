using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public sealed class KVCommit
{
    public Guid CommitId { get; set; } = Guid.NewGuid();

    public Guid? PreviousCommitId { get; set; }

    public string User { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    // Non-null marks this as a schema-migration commit that advances the snapshot's SchemaVersion to this
    // value when applied — even if Changes is empty (a pure version bump). Normal edits leave it null.
    public int? MigrationToVersion { get; set; }

    // Regular KVValue = upsert; KVValue.Tombstone = delete this path and its descendants.
    public KVDictionary Changes { get; set; } = new();
}
