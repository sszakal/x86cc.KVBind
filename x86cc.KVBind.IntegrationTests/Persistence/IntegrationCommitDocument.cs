using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.IntegrationTests.Persistence;

public sealed class IntegrationCommitDocument
{
    public Guid Id { get; set; }

    public Guid AggregateId { get; set; }

    public KVCommit Commit { get; set; } = new();
}
