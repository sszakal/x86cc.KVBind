using System;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    internal void BindRuntime(KVModel model, KVNodeDefinition definition, KVNodeBase? parent = null)
    {
        Bind(model, definition, parent);
    }

    protected virtual void Bind(KVModel model, KVNodeDefinition definition, KVNodeBase? parent = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);

        if (!_isDetached && IsBound && (!ReferenceEquals(Parent, parent) || !ReferenceEquals(Definition, definition)))
            throw new InvalidOperationException("KVNode is already bound to a different context.");

        DetachActiveNestedNodes();
        _nestedSlotModels.Clear();

        _isDetached = false;
        Parent = parent;
        Model = model;
        Definition = definition;

        foreach (var nodeDefinition in definition.Nodes)
        {
            var childNode = nodeDefinition.GetChildNode(this)
                            ?? throw new InvalidOperationException($"Node '{nodeDefinition.SubSegmentPath}' resolved to null.");
            childNode.Bind(model.CreateChildModel(nodeDefinition.SubSegmentPath), nodeDefinition, this);
        }

        foreach (var collectionDefinition in definition.Collections)
        {
            if (string.IsNullOrWhiteSpace(collectionDefinition.SubSegmentPath))
                throw new InvalidOperationException("Collection definitions must define SubSegmentPath.");

            var collectionNode = collectionDefinition.GetCollection(this)
                                 ?? throw new InvalidOperationException($"Collection '{collectionDefinition.SubSegmentPath}' resolved to null.");
            var collectionModel = model.CreateChildModel(collectionDefinition.SubSegmentPath);
            collectionModel.Overlay.RestorePath(collectionModel.DataPath);
            collectionNode.Bind(collectionModel, collectionDefinition, this);
        }

        foreach (var nestedNodeDefinition in definition.NestedNodes)
        {
            if (string.IsNullOrWhiteSpace(nestedNodeDefinition.SubSegmentPath))
                throw new InvalidOperationException("Nested node definitions must define SubSegmentPath.");
            _nestedSlotModels[nestedNodeDefinition.SubSegmentPath] = model.CreateChildModel(nestedNodeDefinition.SubSegmentPath);
        }
    }

    internal void RebindCurrentContextForPatch() => Bind(Model, Definition, Parent);

    private void EnsureBound()
    {
        if (_isDetached || !IsBound)
            throw new InvalidOperationException("KVNode is not bound.");
    }

    private void DetachRuntime()
    {
        if (_isDetached) return;
        DetachActiveNestedNodes();
        _nestedSlotModels.Clear();
        _isDetached = true;
        Parent = null;
        Model = null!;
        Definition = null!;
    }

    private void DetachActiveNestedNodes()
    {
        foreach (var activeNode in _activeNestedNodes.Values)
            activeNode.Node.DetachRuntime();
        _activeNestedNodes.Clear();
    }
}
