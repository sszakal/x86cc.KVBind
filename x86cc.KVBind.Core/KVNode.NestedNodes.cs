using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    protected TBase? GetNestedNode<TBase>(string nestedNodeKey)
        where TBase : KVNestedNode
    {
        EnsureBound();
        ArgumentException.ThrowIfNullOrWhiteSpace(nestedNodeKey);

        var definition = GetNestedNodeDefinition(nestedNodeKey);
        var nestedModel = GetNestedNodeModel(nestedNodeKey);
        var typeToken = KVNestedNode.GetItemType(nestedModel);
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            DetachActiveNestedNode(nestedNodeKey);
            return null;
        }

        if (_activeNestedNodes.TryGetValue(nestedNodeKey, out var activeNode)
            && ReferenceEquals(activeNode.Model, nestedModel)
            && string.Equals(activeNode.TypeToken, typeToken, StringComparison.Ordinal))
        {
            return (TBase)activeNode.Node;
        }

        DetachActiveNestedNode(nestedNodeKey);
        var typeDefinition = definition.GetTypeDefinition(typeToken);
        var node = (KVNestedNode)Activator.CreateInstance(typeDefinition.ModelType)!;
        node.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this);
        _activeNestedNodes[nestedNodeKey] = new ActiveNestedNode(node, typeToken, nestedModel);
        return (TBase)node;
    }

    protected void SetNestedNode<TBase>(string nestedNodeKey, TBase? value)
        where TBase : KVNestedNode
    {
        EnsureBound();
        ArgumentException.ThrowIfNullOrWhiteSpace(nestedNodeKey);

        var definition = GetNestedNodeDefinition(nestedNodeKey);
        var nestedModel = GetNestedNodeModel(nestedNodeKey);
        ClearNestedNodeModel(nestedModel);
        DetachActiveNestedNode(nestedNodeKey);

        if (value is null)
        {
            KVNestedNode.ClearItemType(nestedModel);
            return;
        }

        var typeDefinition = definition.GetTypeDefinition(value.GetType());
        KVNestedNode.SetItemType(nestedModel, typeDefinition.TypeToken);
        value.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this);
        _activeNestedNodes[nestedNodeKey] = new ActiveNestedNode(value, typeDefinition.TypeToken, nestedModel);
    }

    internal KVNestedNode? GetActiveNestedNode(KVNestedNodeDefinition definition, KVModel nestedModel)
    {
        var typeToken = KVNestedNode.GetItemType(nestedModel);
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            DetachActiveNestedNode(definition.SubSegmentPath);
            return null;
        }

        if (_activeNestedNodes.TryGetValue(definition.SubSegmentPath, out var activeNode)
            && ReferenceEquals(activeNode.Model, nestedModel)
            && string.Equals(activeNode.TypeToken, typeToken, StringComparison.Ordinal))
        {
            return activeNode.Node;
        }

        DetachActiveNestedNode(definition.SubSegmentPath);
        var typeDefinition = definition.GetTypeDefinition(typeToken);
        var node = (KVNestedNode)Activator.CreateInstance(typeDefinition.ModelType)!;
        node.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this);
        _activeNestedNodes[definition.SubSegmentPath] = new ActiveNestedNode(node, typeToken, nestedModel);
        return node;
    }

    internal KVModel GetNestedNodeModel(string nestedNodeKey)
    {
        return _nestedSlotModels.TryGetValue(nestedNodeKey, out var slotModel)
            ? slotModel
            : throw new InvalidOperationException($"Nested node slot '{nestedNodeKey}' is not initialized. Ensure the node is fully bound.");
    }

    internal void DetachNestedNodeForPatch(string nestedNodeKey) => DetachActiveNestedNode(nestedNodeKey);

    internal void ClearNestedNodeModelForPatch(KVModel nestedModel) => ClearNestedNodeModel(nestedModel);

    private KVNestedNodeDefinition GetNestedNodeDefinition(string nestedNodeKey)
    {
        return Definition.FindNestedNode(nestedNodeKey)
               ?? throw new InvalidOperationException($"Nested node '{nestedNodeKey}' is not declared under '{Definition.SubSegmentPath}'.");
    }

    private void DetachActiveNestedNode(string nestedNodeKey)
    {
        if (_activeNestedNodes.Remove(nestedNodeKey, out var activeNode))
            activeNode.Node.DetachRuntime();
    }

    private static void ClearNestedNodeModel(KVModel nestedModel)
    {
        var prefix = nestedModel.DataPath;
        var typeKey = KVPath.Combine(prefix, KVNestedNode.TypeKey);
        var overlay = nestedModel.Overlay;

        var toRemove = new List<string>();
        foreach (var key in overlay.Changes.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, prefix) && !string.Equals(key, typeKey, StringComparison.Ordinal))
                toRemove.Add(key);
        }
        foreach (var key in toRemove)
            overlay.Remove(key);

        foreach (var key in overlay.Snapshot.Data.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, prefix)
                && !string.Equals(key, typeKey, StringComparison.Ordinal)
                && !overlay.HasRemovedPath(key))
            {
                overlay.Remove(key);
            }
        }
    }
}
