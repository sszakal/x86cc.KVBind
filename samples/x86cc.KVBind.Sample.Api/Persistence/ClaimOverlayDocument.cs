using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Sample.Api.Persistence;

public sealed class ClaimOverlayDocument
{
    public Guid Id { get; set; }

    public Guid ClaimId { get; set; }

    public string User { get; set; } = string.Empty;

    public KVSnapshot Snapshot { get; set; } = new();

    public Dictionary<string, object?> AddedOrChanged { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> Removed { get; set; } = new(StringComparer.Ordinal);

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid BaseSnapshotVersion => Snapshot.Version;

    public Guid? BaseCommitId => Snapshot.LastCommitId;

    public static ClaimOverlayDocument Create(Guid claimId, string user, KVOverlay overlay)
    {
        return new ClaimOverlayDocument
        {
            Id = Guid.NewGuid(),
            ClaimId = claimId,
            User = user,
            Snapshot = overlay.Snapshot.Clone(),
            AddedOrChanged = new Dictionary<string, object?>(overlay.AddedOrChanged, StringComparer.Ordinal),
            Removed = new HashSet<string>(overlay.Removed, StringComparer.Ordinal),
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public KVOverlay ToOverlay()
    {
        var overlay = KVOverlay.Create(Snapshot.Clone(), User);
        overlay.AddedOrChanged = new Dictionary<string, object?>(AddedOrChanged, StringComparer.Ordinal);
        overlay.Removed = new HashSet<string>(Removed, StringComparer.Ordinal);
        return overlay;
    }

    public void UpdateFrom(KVOverlay overlay)
    {
        Snapshot = overlay.Snapshot.Clone();
        AddedOrChanged = new Dictionary<string, object?>(overlay.AddedOrChanged, StringComparer.Ordinal);
        Removed = new HashSet<string>(overlay.Removed, StringComparer.Ordinal);
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
