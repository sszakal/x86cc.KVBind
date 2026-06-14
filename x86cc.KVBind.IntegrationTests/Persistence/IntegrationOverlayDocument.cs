using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.IntegrationTests.Persistence;

public sealed class IntegrationOverlayDocument
{
    public Guid Id { get; set; }

    public Guid AggregateId { get; set; }

    public string User { get; set; } = string.Empty;

    public KVSnapshot Snapshot { get; set; } = new();

    public KVDictionary Changes { get; set; } = new();

    public static IntegrationOverlayDocument Create(Guid aggregateId, string user, KVOverlay overlay)
    {
        return new IntegrationOverlayDocument
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            User = user,
            Snapshot = overlay.Snapshot.Clone(),
            Changes = new KVDictionary(overlay.Changes)
        };
    }

    public KVOverlay ToOverlay()
    {
        var overlay = KVOverlay.Create(Snapshot.Clone(), User);
        overlay.Changes = new KVDictionary(Changes);
        return overlay;
    }

    public void UpdateFrom(KVOverlay overlay)
    {
        Snapshot = overlay.Snapshot.Clone();
        Changes = new KVDictionary(overlay.Changes);
    }
}
