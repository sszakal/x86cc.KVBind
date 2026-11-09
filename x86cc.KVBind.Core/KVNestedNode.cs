using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract class KVNestedNode : KVNode
{
    internal const string TypeKey = "$type";

    internal override string? ResetKeepKey => x86cc.KVBind.Core.Model.KVPath.Combine(this.Model.DataPath, TypeKey);

    public string? ItemType() => this.Model.Get<string?>(TypeKey);

    internal static string? GetItemType(KVModel model) => model.Get<string?>(TypeKey);

    internal static void SetItemType(KVModel model, string typeToken) => model.Set(TypeKey, typeToken);

    internal static void ClearItemType(KVModel model) => model.Remove(TypeKey);
}
