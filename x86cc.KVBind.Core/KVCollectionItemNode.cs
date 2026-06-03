namespace x86cc.KVBind.Core;

public abstract class KVCollectionItemNode : KVNode
{
    internal const string TypeKey = "$type";

    // Item ID is the last path segment of the model's DataPath — no need to store it separately.
    public string? ItemId()
    {
        var path = Model?.DataPath;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    public string? ItemType() => Model.Get<string?>(TypeKey);

    internal static string? GetItemType(Model.KVModel model) => model.Get<string?>(TypeKey);

    internal static void SetItemType(Model.KVModel model, string typeToken) => model.Set(TypeKey, typeToken);
}
