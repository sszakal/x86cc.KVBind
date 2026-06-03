using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract class KVRootNode : KVNode
{
    internal KVReactionExecutionState ReactionExecutionState { get; } = new();

    public KVModelRoot RootModel() => Model as KVModelRoot
                                    ?? throw new InvalidOperationException("KVRootNode is not bound to KVModelRoot.");

    public string Id { get => RootModel().Id; set => RootModel().Id = value; }

    public string Version { get => RootModel().Version; set => RootModel().Version = value; }

    protected override void Bind(KVModel model, KVNodeDefinition definition, IKVNode? parent = null)
    {
        if (parent is not null)
            throw new InvalidOperationException("KVRootNode cannot be bound as a child node.");

        if (model is not KVModelRoot)
            throw new InvalidOperationException("KVRootNode must be bound to KVModelRoot.");

        Definition = definition;
        base.Bind(model, definition, parent);
    }

    public static TSelf Create<TSelf>(KVModelRoot model, KVNodeDefinition definition) where TSelf : KVRootNode, new()
    {
        var node = new TSelf();
        node.Bind(model, definition);
        return node;
    }

    public static TSelf Create<TSelf>(KVOverlay overlay, KVNodeDefinition definition) where TSelf : KVRootNode, new()
    {
        return Create<TSelf>(new KVModelRoot(overlay), definition);
    }

    public void Clear()
    {
        Model.Overlay.Clear();
        Bind(Model, Definition);
    }

    public void Discard(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Model.Overlay.Discard(KVPath.Normalize(path));
        Bind(Model, Definition);
    }

    public KVCommit CreateCommit(DateTimeOffset timestamp)
    {
        var validation = Validate();
        if (validation.Errors.Count > 0)
        {
            throw new KVChangeSetValidationException(validation.Errors);
        }

        return Model.Overlay.ToCommit(timestamp);
    }

    public KVPatchResult Patch(params KVPatchOperation[] operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVPatchResult(deltas, Validate);
    }

    public KVPatchResult Patch(IEnumerable<KVPatchOperation> operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVPatchResult(deltas, Validate);
    }

    public KVDraftChanges GetAllChanges()
    {
        var deltas = ComputeDeltas(string.Empty, isCollectionItem: false).Flatten();
        return new KVDraftChanges(deltas);
    }

    public KVValidationResult Validate()
    {
        return KVValidationRuntime.Validate(this, GetValidationProfile());
    }

    protected virtual KVValidationProfile GetValidationProfile() => KVDefaultValidationProfile.Instance;
}
