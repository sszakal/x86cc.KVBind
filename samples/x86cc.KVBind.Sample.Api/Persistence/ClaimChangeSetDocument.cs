using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Sample.Api.Persistence;

public sealed class ClaimChangeSetDocument
{
    public Guid Id { get; set; }

    public Guid ClaimId { get; set; }

    public KVCommit Commit { get; set; } = new();
}
