using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode : KVNodeBase
{
    // Lazily allocated — most nodes (collection items, groups without nested nodes) never declare nested
    // nodes, so these stay null and avoid two empty dictionaries per node.
    private Dictionary<string, ActiveNestedNode>? activeNestedNodes;
    private Dictionary<string, KVModel>? nestedSlotModels;
    private bool isDetached;

    public KVNodeBase? Parent { get; private set; }

    public KVModel Model { get; private set; } = null!;

    public KVNodeDefinition Definition { get; protected set; } = null!;

    private bool IsBound => this.Model is not null && this.Definition is not null;

    internal override string GetCanonicalPath() => this.Model?.DataPath ?? string.Empty;

    internal KVChangeDeltaGroup ComputeDeltas(string nodePath, bool isCollectionItem)
        => KVDeltaComputer.Compute(this, nodePath, isCollectionItem);

    internal void EmitChange(string canonicalPath, object? oldValue, object? newValue, KVChangeKind kind = KVChangeKind.Value)
        => KVChangeReactionRuntime.Emit(this, canonicalPath, oldValue, newValue, kind);

    // Notifies reactions that a nested node slot was initialized (newNode set; oldTypeToken is the type it
    // replaced, if any) or dropped (newNode null). The change path is the slot itself.
    internal void EmitNestedNodeChange(string nestedNodeKey, string? oldTypeToken, KVNestedNode? newNode)
    {
        if (string.IsNullOrWhiteSpace(oldTypeToken)) oldTypeToken = null;
        if (oldTypeToken is null && newNode is null) return;

        this.EmitChange(
            KVPath.Combine(this.GetCanonicalPath(), nestedNodeKey),
            oldTypeToken,
            newNode,
            newNode is null ? KVChangeKind.Removed : KVChangeKind.Added);
    }

    private sealed record ActiveNestedNode(KVNestedNode Node, string TypeToken, KVModel Model);
}
