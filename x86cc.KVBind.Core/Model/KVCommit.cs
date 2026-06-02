using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public sealed class KVCommit
{
    public Guid AggregateId { get; set; }

    public Guid CommitId { get; set; } = Guid.NewGuid();

    public Guid? PreviousCommitId { get; set; }

    public string User { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    public Dictionary<string, object?> AddedOrChanged { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> Removed { get; set; } = new(StringComparer.Ordinal);
}
