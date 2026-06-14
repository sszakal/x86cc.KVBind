using System.Linq;
using x86cc.KVBind.Core.Abstractions;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    // Materializes declared defaults into the overlay for any element still unset, recursing as it goes so
    // interdependent defaults resolve (a default nested type pulls in its own field defaults; a seeded
    // collection item pulls in its field defaults). Fill-blanks-only: never overwrites a set value, so it is
    // safe — though only intended — for a freshly created aggregate. Driven from KVRootNode.ApplyDefaults().
    internal void ApplyDefaultsRecursive()
    {
        EnsureBound();

        foreach (var field in Definition.Fields)
        {
            if (field.HasDefault && !IsFieldSet(field.SubSegmentPath))
                SetFieldForPatch(field.SubSegmentPath, field.DefaultValue);
        }

        foreach (var nodeDef in Definition.Nodes)
        {
            nodeDef.GetChildNode(this).ApplyDefaultsRecursive();
        }

        foreach (var collDef in Definition.Collections)
        {
            var collection = collDef.GetCollection(this);
            if (collDef.DefaultSeed is not null && collection.GetActiveItemIds().Count == 0)
                collDef.DefaultSeed(collection);

            foreach (var itemId in collection.GetActiveItemIds().ToArray())
            {
                if (collection.GetById(itemId) is { } item)
                    item.ApplyDefaultsRecursive();
            }
        }

        foreach (var nestedDef in Definition.NestedNodes)
        {
            if (string.IsNullOrEmpty(nestedDef.DefaultTypeToken))
                continue;

            var slotModel = GetNestedNodeModel(nestedDef.SubSegmentPath);
            if (!string.IsNullOrWhiteSpace(KVNestedNode.GetItemType(slotModel)))
                continue; // already initialized — leave it be

            InitNestedNodeToToken(nestedDef.SubSegmentPath, nestedDef.DefaultTypeToken).ApplyDefaultsRecursive();
        }
    }

    private bool IsFieldSet(string fieldKey)
        => Model.TryGetValue(fieldKey, out var value) && value?.Value is not null;
}
