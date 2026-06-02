using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Sample.Api.Persistence;

public sealed class ClaimSnapshotDocument
{
    public Guid Id { get; set; }

    public KVSnapshot Snapshot { get; set; } = new();
}
