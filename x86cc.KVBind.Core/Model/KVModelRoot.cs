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

    public KVSnapshot Snapshot => this.Overlay.Snapshot;

    public static KVModelRoot Create(KVOverlay overlay) => new(overlay);
}
