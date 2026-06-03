using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api.Claims;

namespace x86cc.KVBind.Sample.Api.Persistence;

public sealed class ClaimChangeSetDocument
{
    public Guid Id { get; set; }

    public Guid ClaimId { get; set; }

    public KVCommit Commit { get; set; } = new();

    public IReadOnlyList<ClaimChangeResponse> Changes { get; set; } = [];
}
