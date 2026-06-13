using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public sealed class KVCommit
{
    public Guid CommitId { get; set; } = Guid.NewGuid();

    public Guid? PreviousCommitId { get; set; }

    public string User { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    // Regular KVValue = upsert; KVValue.Tombstone = delete this path and its descendants.
    public Dictionary<string, KVValue> Changes { get; set; } = new(StringComparer.Ordinal);
}
