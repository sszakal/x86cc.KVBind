namespace x86cc.KVBind.Core;

public abstract class KVCollectionItemNode : KVNode
{
    internal const string IdKey = "$id";
    internal const string TypeKey = "$type";

    public string? ItemId() => Model.Get<string?>(IdKey);

    public string? ItemType() => Model.Get<string?>(TypeKey);

    internal static string? GetItemId(Model.KVModel model) => model.Get<string?>(IdKey);

    internal static void SetItemId(Model.KVModel model, string itemId) => model.Set(IdKey, itemId);

    internal static string? GetItemType(Model.KVModel model) => model.Get<string?>(TypeKey);

    internal static void SetItemType(Model.KVModel model, string typeToken) => model.Set(TypeKey, typeToken);
}
