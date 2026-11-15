using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Schema;

/// <summary>
/// Resolves the references a recursive definition leaves in its schema (<see cref="KVItemTypeSchema.Ref"/>).
/// </summary>
/// <example>
/// <code>
/// var types = KVSchemaReferences.Index(schema);           // once per document
/// var node = KVSchemaReferences.NodeOf(itemType, types);  // the item type's node, following a Ref
/// </code>
/// </example>
public static class KVSchemaReferences
{
    /// <summary>The item and nested types of the document that carry an <see cref="KVItemTypeSchema.Id"/>, by id.</summary>
    public static IReadOnlyDictionary<string, KVItemTypeSchema> Index(KVSchemaDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var index = new Dictionary<string, KVItemTypeSchema>(StringComparer.Ordinal);
        Collect(document.Root, index);
        return index;
    }

    /// <summary>The node of an item or nested type: its own, or the one its <see cref="KVItemTypeSchema.Ref"/> names.</summary>
    public static KVNodeSchema NodeOf(KVItemTypeSchema type, IReadOnlyDictionary<string, KVItemTypeSchema> index)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(index);

        if (type.Ref is null)
        {
            return type.Node;
        }

        return index.TryGetValue(type.Ref, out var target)
            ? target.Node
            : throw new InvalidOperationException($"Schema type '{type.Token}' refers to '{type.Ref}', which the document does not declare.");
    }

    private static void Collect(KVNodeSchema node, Dictionary<string, KVItemTypeSchema> index)
    {
        foreach (var group in node.Groups)
        {
            Collect(group, index);
        }

        foreach (var collection in node.Collections)
        {
            CollectTypes(collection.ItemTypes, index);
        }

        foreach (var nested in node.NestedNodes)
        {
            CollectTypes(nested.Types, index);
        }
    }

    private static void CollectTypes(List<KVItemTypeSchema> types, Dictionary<string, KVItemTypeSchema> index)
    {
        foreach (var type in types)
        {
            if (type.Id is not null)
            {
                index[type.Id] = type;
            }

            // A reference has an empty node, so the walk ends there.
            Collect(type.Node, index);
        }
    }
}
