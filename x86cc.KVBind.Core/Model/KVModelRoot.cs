namespace x86cc.KVBind.Core.Model;

public class KVModelRoot : KVModel
{
    public KVModelRoot()
    {
    }

    public KVModelRoot(KVOverlay overlay)
        : base(overlay)
    {
    }

    public static KVModelRoot Create(KVOverlay overlay) => new(overlay);

    public KVSnapshot Snapshot => Overlay.Snapshot;
}
