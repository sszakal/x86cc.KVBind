using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.IntegrationTests.Persistence;

public sealed class IntegrationSnapshotDocument
{
    public Guid Id { get; set; }

    public KVSnapshot Snapshot { get; set; } = new();
}
