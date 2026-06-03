using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.IntegrationTests.Persistence;

public sealed class IntegrationOverlayDocument
{
    public Guid Id { get; set; }

    public Guid AggregateId { get; set; }

    public string User { get; set; } = string.Empty;

    public KVSnapshot Snapshot { get; set; } = new();

    public Dictionary<string, KVValue> Changes { get; set; } = new(StringComparer.Ordinal);

    public static IntegrationOverlayDocument Create(Guid aggregateId, string user, KVOverlay overlay)
    {
        return new IntegrationOverlayDocument
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            User = user,
            Snapshot = overlay.Snapshot.Clone(),
            Changes = new Dictionary<string, KVValue>(overlay.Changes, StringComparer.Ordinal)
        };
    }

    public KVOverlay ToOverlay()
    {
        var overlay = KVOverlay.Create(Snapshot.Clone(), User);
        overlay.Changes = new Dictionary<string, KVValue>(Changes, StringComparer.Ordinal);
        return overlay;
    }

    public void UpdateFrom(KVOverlay overlay)
    {
        Snapshot = overlay.Snapshot.Clone();
        Changes = new Dictionary<string, KVValue>(overlay.Changes, StringComparer.Ordinal);
    }
}
