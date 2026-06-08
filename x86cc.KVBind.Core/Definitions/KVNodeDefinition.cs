using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public class KVNodeDefinition : KVDefinition
{
    // Lazily-built index over Fields by SubSegmentPath. Definitions are built once via the builder
    // and then reused, so the index is materialized on first lookup and cached thereafter.
    private Dictionary<string, KVFieldDefinition>? _fieldsByKey;

    public List<KVFieldDefinition> Fields { get; } = new();
    public List<KVNodeDefinition> Nodes { get; } = new();
    public List<KVCollectionDefinition> Collections { get; } = new();
    public List<KVNestedNodeDefinition> NestedNodes { get; } = new();
    public List<KVValidationRegistration> ValidationRegistrations { get; } = new();
    internal List<KVChangeReactionDescriptor> ChangeReactions { get; } = new();
    public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
    public bool? IsResettable { get; set; }
    public Func<KVNode, KVNode> GetChildNode { get; init; } = _ => throw new NotImplementedException();

    // O(1) field lookup by SubSegmentPath, replacing an allocating O(n) List.Find on the read hot path.
    public KVFieldDefinition? FindField(string subSegmentPath)
    {
        var index = _fieldsByKey ??= BuildFieldIndex();
        return index.TryGetValue(subSegmentPath, out var field) ? field : null;
    }

    private Dictionary<string, KVFieldDefinition> BuildFieldIndex()
    {
        var index = new Dictionary<string, KVFieldDefinition>(Fields.Count, StringComparer.Ordinal);
        foreach (var field in Fields)
            index[field.SubSegmentPath] = field;
        return index;
    }
}
