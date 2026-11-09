using System.Linq;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    // Materializes declared defaults into the overlay for any element still unset, recursing as it goes so
    // interdependent defaults resolve (a default nested type pulls in its own field defaults; a seeded
    // collection item pulls in its field defaults). Fill-blanks-only: never overwrites a set value, so it is
    // safe — though only intended — for a newly born subgraph. Driven from KVRootNode.ApplyDefaults() (via
    // CreateNew) and whenever a collection item is created or a nested node is initialized (Create/ADD/INIT).
    internal void ApplyDefaultsRecursive()
    {
        this.EnsureBound();

        // When inheritance is active (a child bound with a parent), inherited root members are read-only and
        // owned by the parent — skip them so we never try to write a default into a read-only path. On a
        // parentless master (no inheritance) the same members are editable and default normally.
        var hasInheritance = this.Model.Overlay.HasInheritance;

        foreach (var field in this.Definition.Fields)
        {
            if (hasInheritance && field.IsInherited) continue;
            if (field.HasDefault && !this.IsFieldSet(field.SubSegmentPath))
                this.SetFieldForPatch(field.SubSegmentPath, field.DefaultValue);
        }

        foreach (var nodeDef in this.Definition.Nodes)
        {
            if (hasInheritance && nodeDef.IsInherited) continue;
            nodeDef.GetChildNode(this).ApplyDefaultsRecursive();
        }

        foreach (var collDef in this.Definition.Collections)
        {
            if (hasInheritance && collDef.IsInherited) continue;
            var collection = collDef.GetCollection(this);

            // Seeding belongs to the collection's birth, i.e. while it is still empty: a collection that
            // already holds items (an existing aggregate, or one whose seeded items were later removed and
            // committed) is never re-seeded. Each seeded item gets its own defaults when it is created.
            if (collection.GetActiveItemIds().Count == 0)
            {
                collDef.DefaultSeed?.Invoke(collection);
                foreach (var seed in collDef.Seeds)
                {
                    var item = (KVCollectionItemNode)collection.Create(seed.ItemId, seed.ItemDefinition.TypeToken);
                    seed.Configure?.Invoke(item);
                }
            }

            foreach (var itemId in collection.GetActiveItemIds().ToArray())
            {
                if (collection.GetById(itemId) is { } item)
                    item.ApplyDefaultsRecursive();
            }
        }

        foreach (var nestedDef in this.Definition.NestedNodes)
        {
            if (hasInheritance && nestedDef.IsInherited) continue;
            if (string.IsNullOrEmpty(nestedDef.DefaultTypeToken))
                continue;

            var slotModel = this.GetNestedNodeModel(nestedDef.SubSegmentPath);
            if (!string.IsNullOrWhiteSpace(KVNestedNode.GetItemType(slotModel)))
                continue; // already initialized — leave it be

            this.InitNestedNodeToToken(nestedDef.SubSegmentPath, nestedDef.DefaultTypeToken).ApplyDefaultsRecursive();
        }
    }

    private bool IsFieldSet(string fieldKey)
        => this.Model.TryGetValue(fieldKey, out var value) && value?.Value is not null;
}
