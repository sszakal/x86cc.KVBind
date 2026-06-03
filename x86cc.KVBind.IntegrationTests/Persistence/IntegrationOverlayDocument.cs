using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.IntegrationTests.Persistence;

public sealed class IntegrationOverlayDocument
{
    public Guid Id { get; set; }

    public Guid AggregateId { get; set; }

    public string User { get; set; } = string.Empty;

    public KVSnapshot Snapshot { get; set; } = new();

    public Dictionary<string, KVValue> AddedOrChanged { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> Removed { get; set; } = new(StringComparer.Ordinal);

    public static IntegrationOverlayDocument Create(Guid aggregateId, string user, KVOverlay overlay)
    {
        return new IntegrationOverlayDocument
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            User = user,
            Snapshot = overlay.Snapshot.Clone(),
            AddedOrChanged = new Dictionary<string, KVValue>(overlay.AddedOrChanged, StringComparer.Ordinal),
            Removed = new HashSet<string>(overlay.Removed, StringComparer.Ordinal)
        };
    }

    public KVOverlay ToOverlay()
    {
        var overlay = KVOverlay.Create(Snapshot.Clone(), User);
        overlay.AddedOrChanged = new Dictionary<string, KVValue>(AddedOrChanged, StringComparer.Ordinal);
        overlay.Removed = new HashSet<string>(Removed, StringComparer.Ordinal);
        return overlay;
    }

    public void UpdateFrom(KVOverlay overlay)
    {
        Snapshot = overlay.Snapshot.Clone();
        AddedOrChanged = new Dictionary<string, KVValue>(overlay.AddedOrChanged, StringComparer.Ordinal);
        Removed = new HashSet<string>(overlay.Removed, StringComparer.Ordinal);
    }
}
