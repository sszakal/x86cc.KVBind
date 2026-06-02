using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public class KVNodeDefinition : KVDefinition
{
    public List<KVFieldDefinition> Fields { get; } = new();
    public List<KVNodeDefinition> Nodes { get; } = new();
    public List<KVCollectionDefinition> Collections { get; } = new();
    public List<KVNestedNodeDefinition> NestedNodes { get; } = new();
    public List<KVValidationRegistration> ValidationRegistrations { get; } = new();
    internal List<KVChangeReactionDescriptor> ChangeReactions { get; } = new();
    public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
    public bool? IsResettable { get; set; }
    public Func<KVNode, KVNode> GetChildNode { get; init; } = _ => throw new NotImplementedException();
}
