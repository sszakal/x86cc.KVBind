using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    internal KVNestedNode? GetActiveNestedNode(KVNestedNodeDefinition definition, KVModel nestedModel)
    {
        var typeToken = KVNestedNode.GetItemType(nestedModel);
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            this.DetachActiveNestedNode(definition.SubSegmentPath);
            return null;
        }

        if (this.activeNestedNodes is not null
            && this.activeNestedNodes.TryGetValue(definition.SubSegmentPath, out var activeNode)
            && ReferenceEquals(activeNode.Model, nestedModel)
            && string.Equals(activeNode.TypeToken, typeToken, StringComparison.Ordinal))
        {
            return activeNode.Node;
        }

        this.DetachActiveNestedNode(definition.SubSegmentPath);
        var typeDefinition = definition.GetTypeDefinition(typeToken);
        var node = (KVNestedNode)Activator.CreateInstance(typeDefinition.ModelType)!;
        node.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this);
        (this.activeNestedNodes ??= new(StringComparer.Ordinal))[definition.SubSegmentPath] = new ActiveNestedNode(node, typeToken, nestedModel);
        return node;
    }

    // Initializes a nested-node slot to a subtype by token (used by ApplyDefaults). Mirrors SetNestedNode
    // but resolves the type from the token rather than an instance.
    internal KVNestedNode InitNestedNodeToToken(string nestedNodeKey, string token)
    {
        this.EnsureBound();
        var definition = this.GetNestedNodeDefinition(nestedNodeKey);
        var nestedModel = this.GetNestedNodeModel(nestedNodeKey);
        this.DetachActiveNestedNode(nestedNodeKey);

        var typeDefinition = definition.GetTypeDefinition(token);
        var node = (KVNestedNode)Activator.CreateInstance(typeDefinition.ModelType)!;
        KVNestedNode.SetItemType(nestedModel, typeDefinition.TypeToken);
        node.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this);
        (this.activeNestedNodes ??= new(StringComparer.Ordinal))[nestedNodeKey] = new ActiveNestedNode(node, typeDefinition.TypeToken, nestedModel);
        return node;
    }

    internal KVModel GetNestedNodeModel(string nestedNodeKey)
    {
        return this.nestedSlotModels is not null && this.nestedSlotModels.TryGetValue(nestedNodeKey, out var slotModel)
            ? slotModel
            : throw new InvalidOperationException($"Nested node slot '{nestedNodeKey}' is not initialized. Ensure the node is fully bound.");
    }

    internal void DetachNestedNodeForPatch(string nestedNodeKey) => this.DetachActiveNestedNode(nestedNodeKey);

    internal void ClearNestedNodeModelForPatch(KVModel nestedModel) => ClearNestedNodeModel(nestedModel);

    protected TBase? GetNestedNode<TBase>(string nestedNodeKey)
        where TBase : KVNestedNode
    {
        this.EnsureBound();
        ArgumentException.ThrowIfNullOrWhiteSpace(nestedNodeKey);

        var definition = this.GetNestedNodeDefinition(nestedNodeKey);
        var nestedModel = this.GetNestedNodeModel(nestedNodeKey);
        var typeToken = KVNestedNode.GetItemType(nestedModel);
        if (string.IsNullOrWhiteSpace(typeToken))
        {
            this.DetachActiveNestedNode(nestedNodeKey);
            return null;
        }

        if (this.activeNestedNodes is not null
            && this.activeNestedNodes.TryGetValue(nestedNodeKey, out var activeNode)
            && ReferenceEquals(activeNode.Model, nestedModel)
            && string.Equals(activeNode.TypeToken, typeToken, StringComparison.Ordinal))
        {
            return (TBase)activeNode.Node;
        }

        this.DetachActiveNestedNode(nestedNodeKey);
        var typeDefinition = definition.GetTypeDefinition(typeToken);
        var node = (KVNestedNode)Activator.CreateInstance(typeDefinition.ModelType)!;
        node.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this);
        (this.activeNestedNodes ??= new(StringComparer.Ordinal))[nestedNodeKey] = new ActiveNestedNode(node, typeToken, nestedModel);
        return (TBase)node;
    }

    protected void SetNestedNode<TBase>(string nestedNodeKey, TBase? value)
        where TBase : KVNestedNode
    {
        this.EnsureBound();
        ArgumentException.ThrowIfNullOrWhiteSpace(nestedNodeKey);

        var definition = this.GetNestedNodeDefinition(nestedNodeKey);
        var nestedModel = this.GetNestedNodeModel(nestedNodeKey);
        var oldTypeToken = KVNestedNode.GetItemType(nestedModel);
        ClearNestedNodeModel(nestedModel);
        this.DetachActiveNestedNode(nestedNodeKey);

        if (value is null)
        {
            KVNestedNode.ClearItemType(nestedModel);
            this.EmitNestedNodeChange(nestedNodeKey, oldTypeToken, newNode: null);
            return;
        }

        var typeDefinition = definition.GetTypeDefinition(value.GetType());
        KVNestedNode.SetItemType(nestedModel, typeDefinition.TypeToken);
        value.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this);
        (this.activeNestedNodes ??= new(StringComparer.Ordinal))[nestedNodeKey] = new ActiveNestedNode(value, typeDefinition.TypeToken, nestedModel);

        // The slot was cleared above, so this is a newly born node: apply its declared defaults, as INIT does.
        value.ApplyDefaultsRecursive();
        this.EmitNestedNodeChange(nestedNodeKey, oldTypeToken, value);
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

    private KVNestedNodeDefinition GetNestedNodeDefinition(string nestedNodeKey)
    {
        return this.Definition.FindNestedNode(nestedNodeKey)
               ?? throw new InvalidOperationException($"Nested node '{nestedNodeKey}' is not declared under '{this.Definition.SubSegmentPath}'.");
    }

    private void DetachActiveNestedNode(string nestedNodeKey)
    {
        if (this.activeNestedNodes is not null && this.activeNestedNodes.Remove(nestedNodeKey, out var activeNode))
            activeNode.Node.DetachRuntime();
    }
}
