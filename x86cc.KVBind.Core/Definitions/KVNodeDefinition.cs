using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public class KVNodeDefinition : KVDefinition
{
    // Lazily-built indexes by SubSegmentPath. Definitions are built once via the builder and then reused,
    // so each index is materialized on first lookup and cached thereafter.
    private Dictionary<string, KVFieldDefinition>? _fieldsByKey;
    private Dictionary<string, KVNodeDefinition>? _nodesByKey;
    private Dictionary<string, KVCollectionDefinition>? _collectionsByKey;
    private Dictionary<string, KVNestedNodeDefinition>? _nestedNodesByKey;

    public List<KVFieldDefinition> Fields { get; } = new();
    public List<KVNodeDefinition> Nodes { get; } = new();
    public List<KVCollectionDefinition> Collections { get; } = new();
    public List<KVNestedNodeDefinition> NestedNodes { get; } = new();
    public List<KVValidationRegistration> ValidationRegistrations { get; } = new();
    internal List<KVChangeReactionDescriptor> ChangeReactions { get; } = new();
    public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
    public bool? IsResettable { get; set; }
    public Func<KVNode, KVNode> GetChildNode { get; init; } = _ => throw new NotImplementedException();

    // Lookups by SubSegmentPath, replacing allocating O(n) List.Find on warm paths (read, patch resolution,
    // validation, nested-node access). Indexes are lazy and cached; the lists remain authoritative for
    // iteration and declaration order.
    public KVFieldDefinition? FindField(string subSegmentPath)
        => (_fieldsByKey ??= BuildIndex(Fields)).GetValueOrDefault(subSegmentPath);

    public KVNodeDefinition? FindNode(string subSegmentPath)
        => (_nodesByKey ??= BuildIndex(Nodes)).GetValueOrDefault(subSegmentPath);

    public KVCollectionDefinition? FindCollection(string subSegmentPath)
        => (_collectionsByKey ??= BuildIndex(Collections)).GetValueOrDefault(subSegmentPath);

    public KVNestedNodeDefinition? FindNestedNode(string subSegmentPath)
        => (_nestedNodesByKey ??= BuildIndex(NestedNodes)).GetValueOrDefault(subSegmentPath);

    // The builder removes any existing entry before adding, so each SubSegmentPath maps to one definition.
    private static Dictionary<string, T> BuildIndex<T>(List<T> definitions) where T : KVDefinition
    {
        var index = new Dictionary<string, T>(definitions.Count, StringComparer.Ordinal);
        foreach (var definition in definitions)
            index[definition.SubSegmentPath] = definition;
        return index;
    }
}
