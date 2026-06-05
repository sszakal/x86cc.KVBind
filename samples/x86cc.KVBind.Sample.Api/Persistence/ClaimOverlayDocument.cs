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

    public Guid BaseSnapshotVersion => Snapshot.Version;

    public Guid? BaseCommitId => Snapshot.LastCommitId;

    public static ClaimOverlayDocument Create(Guid claimId, string user, KVOverlay overlay)
    {
        var document = new ClaimOverlayDocument
        {
            Id = Guid.NewGuid(),
            ClaimId = claimId,
            User = user,
        };
        document.UpdateFrom(overlay);
        return document;
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
