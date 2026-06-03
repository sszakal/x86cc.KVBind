using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode : KVNodeBase
{
    private readonly Dictionary<string, ActiveNestedNode> _activeNestedNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, KVModel> _nestedSlotModels = new(StringComparer.Ordinal);
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
