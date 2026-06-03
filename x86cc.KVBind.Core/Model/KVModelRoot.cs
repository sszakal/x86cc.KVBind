using x86cc.KVBind.Core;

namespace x86cc.KVBind.Core.Model;

public class KVModelRoot : KVModel
{
    public KVModelRoot()
    {
    }

    public KVModelRoot(KVOverlay overlay)
        : base(overlay, string.Empty)
    {
    }

    public static KVModelRoot Create(KVOverlay overlay) => new(overlay);

    public KVSnapshot Snapshot => Overlay.Snapshot;

    public KVChangeDeltaGroup ComputeDeltas() => ComputeNodeDeltas(string.Empty, isCollectionItem: false);

    public string Id { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;
}
