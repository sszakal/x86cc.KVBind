using System;

namespace x86cc.KVBind.Core.Model;

public class KVCollectionModel : KVModel
{
    public KVCollectionModel()
    {
    }

    internal KVCollectionModel(KVOverlay overlay, string dataPath)
        : base(overlay, dataPath)
    {
    }

    public KVModel EnsureItemModel(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

        if (!ChildModels.TryGetValue(key, out var child))
        {
            var item = new KVModel(Overlay, string.IsNullOrWhiteSpace(DataPath) ? key : DataPath + "/" + key);
            ChildModels[key] = item;
            UnmarkChildRemoved(key);
            return item;
        }

        UnmarkChildRemoved(key);

        return child;
    }
}
