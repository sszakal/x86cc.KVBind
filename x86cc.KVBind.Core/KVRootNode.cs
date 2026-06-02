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

    protected override void Bind(
        KVModel model,
        KVNodeDefinition definition,
        IKVNode? parent = null,
        string? subSegmentOverride = null,
        string? storagePathOverride = null)
    {
        if (parent is not null)
            throw new InvalidOperationException("KVRootNode cannot be bound as a child node.");

        if (model is not KVModelRoot rootModel)
            throw new InvalidOperationException("KVRootNode must be bound to KVModelRoot.");

        base.Bind(rootModel, definition, parent, subSegmentOverride, storagePathOverride);
    }

    private void Bind(KVModelRoot rootModel)
    {
        var definition = rootModel.Definition ?? throw new InvalidOperationException("KVModelRoot is not attached to a definition.");
        Bind((KVModel)rootModel, definition);
    }

    public static TSelf Create<TSelf>(KVModelRoot model) where TSelf: KVRootNode, new()
    {
        var node = new TSelf();
        node.Bind(model);
        return node;
    }

    public static TSelf Create<TSelf>(KVModelRoot model, KVNodeDefinition definition) where TSelf: KVRootNode, new()
    {
        model.AttachDefinition(definition);
        return Create<TSelf>(model);
    }
    
    public void Clear()
    {
        var rootModel = RootModel();
        rootModel.ClearDraft();
        Bind(rootModel);
    }

    public void Discard(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var rootModel = RootModel();
        rootModel.DiscardDraftPath(path);
        Bind(rootModel);
    }

    public KVCommit CreateCommit(DateTimeOffset timestamp)
    {
        var validation = Validate();
        if (validation.Errors.Count > 0)
        {
            throw new KVChangeSetValidationException(validation.Errors);
        }

        return RootModel().CreateCommit(timestamp);
    }

    public KVPatchResult Patch(params KVPatchOperation[] operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = RootModel().ComputeDeltas().Flatten();
        return new KVPatchResult(deltas, Validate);
    }

    public KVPatchResult Patch(IEnumerable<KVPatchOperation> operations)
    {
        KVPatchRuntime.Apply(this, operations);
        var deltas = RootModel().ComputeDeltas().Flatten();
        return new KVPatchResult(deltas, Validate);
    }
    
    public KVDraftChanges GetAllChanges()
    {
        var deltas = RootModel().ComputeDeltas().Flatten();
        return new KVDraftChanges(deltas);
    }
    
    public KVValidationResult Validate()
    {
        return KVValidationRuntime.Validate(this, GetValidationProfile());
    }

    protected virtual KVValidationProfile GetValidationProfile() => KVDefaultValidationProfile.Instance;

}
