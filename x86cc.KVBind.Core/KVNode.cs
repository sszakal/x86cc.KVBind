using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode : KVNodeBase
{
    // Lazily allocated — most nodes (collection items, groups without nested nodes) never declare nested
    // nodes, so these stay null and avoid two empty dictionaries per node.
    private Dictionary<string, ActiveNestedNode>? _activeNestedNodes;
    private Dictionary<string, KVModel>? _nestedSlotModels;
    private bool _isDetached;

    public KVNodeBase? Parent { get; private set; }
    public KVModel Model { get; private set; } = null!;
    public KVNodeDefinition Definition { get; protected set; } = null!;

    private bool IsBound => Model is not null && Definition is not null;

    internal override string GetCanonicalPath() => Model?.DataPath ?? string.Empty;

    internal KVChangeDeltaGroup ComputeDeltas(string nodePath, bool isCollectionItem)
        => KVDeltaComputer.Compute(this, nodePath, isCollectionItem);

    internal void EmitChange(string canonicalPath, object? oldValue, object? newValue)
        => KVChangeReactionRuntime.Emit(this, canonicalPath, oldValue, newValue);

    private sealed record ActiveNestedNode(KVNestedNode Node, string TypeToken, KVModel Model);
}
