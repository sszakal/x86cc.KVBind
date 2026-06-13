using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Sample.Api.Persistence;

public sealed class ClaimOverlayDocument
{
    public Guid Id { get; set; }

    public Guid ClaimId { get; set; }

    public string User { get; set; } = string.Empty;

    public KVSnapshot Snapshot { get; set; } = new();

    public Dictionary<string, KVValue> Changes { get; set; } = new(StringComparer.Ordinal);

    public DateTimeOffset UpdatedAt { get; set; }

    // ── Rebase state ──────────────────────────────────────────────────────────
    public bool IsRebasing { get; set; }

    // Frozen target snapshot (V2) the rebase resolves against. Null unless rebasing.
    public KVSnapshot? RebaseTarget { get; set; }

    public List<KVConflict> Conflicts { get; set; } = [];

    public Guid? BaseCommitId => Snapshot.LastCommitId;

    public static ClaimOverlayDocument Create(Guid claimId, string user, KVOverlay overlay)
    {
        var document = new ClaimOverlayDocument
        {
            Id = DraftId(claimId, user),
            ClaimId = claimId,
            User = user,
        };
        document.UpdateFrom(overlay);
        return document;
    }

    // A user has at most one open draft per claim, so (ClaimId, User) is the identity. Marten keys a
    // document by a single member, so we derive a deterministic Guid from the pair — same claim+user
    // always maps to the same document, which makes uniqueness structural (no separate index needed).
    public static Guid DraftId(Guid claimId, string user)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{claimId:D}:{user}"));
        return new Guid(bytes);
    }

    public KVOverlay ToOverlay()
    {
        var overlay = KVOverlay.Create(Snapshot.Clone(), User);
        overlay.Changes = new Dictionary<string, KVValue>(Changes, StringComparer.Ordinal);
        if (IsRebasing && RebaseTarget is not null)
            overlay.RestoreRebaseState(RebaseTarget.Clone(), Conflicts);
        return overlay;
    }

    public void UpdateFrom(KVOverlay overlay)
    {
        Snapshot = overlay.Snapshot.Clone();
        Changes = new Dictionary<string, KVValue>(overlay.Changes, StringComparer.Ordinal);
        IsRebasing = overlay.IsRebasing;
        RebaseTarget = overlay.RebaseTarget?.Clone();
        Conflicts = [.. overlay.Conflicts];
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
