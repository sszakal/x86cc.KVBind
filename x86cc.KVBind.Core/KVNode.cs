using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal interface IKVNodeCanonicalPath
{
    string GetCanonicalPath();
}

public abstract class KVNode: IKVNode, IKVNodeCanonicalPath
{
    private readonly Dictionary<string, ActiveNestedNode> _activeNestedNodes = new(StringComparer.Ordinal);
    private bool _isDetached;

    public IKVNode? Parent { get; private set; }
    public KVModel Model { get; private set; } = null!;
    public KVNodeDefinition Definition { get; private set; } = null!;
    private bool IsBound => Model is not null && Definition is not null;

    string IKVNodeCanonicalPath.GetCanonicalPath() => GetCanonicalPath();

    internal void BindRuntime(KVModel model, KVNodeDefinition definition, IKVNode? parent = null)
    {
        Bind(model, definition, parent);
    }

    protected virtual void Bind(KVModel model, KVNodeDefinition definition, IKVNode? parent = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);

        if (!_isDetached && IsBound && (!ReferenceEquals(Parent, parent) || !ReferenceEquals(Definition, definition)))
        {
            throw new InvalidOperationException("KVNode is already bound to a different context.");
        }

        DetachActiveNestedNodes();

        _isDetached = false;
        Parent = parent;
        Model = model;
        Definition = definition;

        foreach (var nodeDefinition in definition.Nodes)
        {
            var childNode = nodeDefinition.GetChildNode(this)
                            ?? throw new InvalidOperationException($"Node '{nodeDefinition.SubSegmentPath}' resolved to null.");
            var childModel = model.EnsureChildModel(nodeDefinition.SubSegmentPath);
            childNode.Bind(childModel, nodeDefinition, this);
        }

        foreach (var collectionDefinition in definition.Collections)
        {
            if (string.IsNullOrWhiteSpace(collectionDefinition.SubSegmentPath))
            {
                throw new InvalidOperationException("Collection definitions must define SubSegmentPath.");
            }

            var collectionNode = collectionDefinition.GetCollection(this) ?? throw new InvalidOperationException($"Collection '{collectionDefinition.SubSegmentPath}' resolved to null.");
            var collectionModel = model.EnsureCollectionModel(collectionDefinition.SubSegmentPath);
            collectionNode.Bind(collectionModel, collectionDefinition, this);
        }

        foreach (var nestedNodeDefinition in definition.NestedNodes)
        {
            if (string.IsNullOrWhiteSpace(nestedNodeDefinition.SubSegmentPath))
            {
                throw new InvalidOperationException("Nested node definitions must define SubSegmentPath.");
            }

            model.EnsureChildModel(nestedNodeDefinition.SubSegmentPath);
        }
    }

    private void EnsureBound()
    {
        if (_isDetached || !IsBound)
            throw new InvalidOperationException("KVNode is not bound.");
    }

    private void DetachRuntime()
    {
        if (_isDetached)
        {
            return;
        }

        DetachActiveNestedNodes();
        _isDetached = true;
        Parent = null;
        Model = null!;
        Definition = null!;
    }

    private void DetachActiveNestedNodes()
    {
        foreach (var activeNode in _activeNestedNodes.Values)
        {
            activeNode.Node.DetachRuntime();
        }

        _activeNestedNodes.Clear();
    }

    private KVFieldDefinition GetFieldDefinition(string subSegmentPath)
    {
        return Definition.Fields.Find(f => string.Equals(f.SubSegmentPath, subSegmentPath, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Field '{subSegmentPath}' is not declared under '{Definition.SubSegmentPath}'.");
    }

    private void EnsureFieldDefined(string subSegmentPath)
    {
        _ = GetFieldDefinition(subSegmentPath);
    }

    protected TValue GetField<TValue>(string fieldKey)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        var fieldDefinition = GetFieldDefinition(fieldKey);
        if (fieldDefinition.AllowedValues is not null && Model.TryGetValue(fieldKey, out var storedValue) && storedValue is not null)
        {
            var storedObject = storedValue.Value;
            try
            {
                return (TValue)fieldDefinition.AllowedValues.DenormalizeFromStorage(storedObject, typeof(TValue))!;
            }
            catch (InvalidOperationException) when (storedObject is TValue typed)
            {
                return typed;
            }
        }

        return Model.Get<TValue>(fieldKey);
    }

    protected void SetField<TValue>(string fieldKey, TValue value)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        EnsureFieldDefined(fieldKey);
        SetFieldCore(fieldKey, value);
    }

    internal void SetFieldForPatch(string fieldKey, object? value)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        EnsureFieldDefined(fieldKey);
        SetFieldCore(fieldKey, value);
    }

    internal void RemoveFieldForPatch(string fieldKey)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        EnsureFieldDefined(fieldKey);
        var oldValue = Model.Get<object?>(fieldKey);
        if (!Model.Remove(fieldKey))
        {
            return;
        }

        EmitChange(KVPath.Combine(GetCanonicalPath(), fieldKey), oldValue, newValue: null);
    }

    private void SetFieldCore(string fieldKey, object? value)
    {
        var fieldDefinition = GetFieldDefinition(fieldKey);
        var oldValue = Model.Get<object?>(fieldKey);
        var storageValue = value;
        if (fieldDefinition.AllowedValues is not null)
        {
            try
            {
                storageValue = fieldDefinition.AllowedValues.NormalizeForStorage(value);
            }
            catch (InvalidOperationException) when (value is string)
            {
                // Keep unknown tokens in the draft so validation can report allowed_values.
            }
        }
        if (Equals(oldValue, storageValue))
        {
            return;
        }

        Model.SetValue(fieldKey, KVValue.FromObject(storageValue));
        EmitChange(KVPath.Combine(GetCanonicalPath(), fieldKey), oldValue, storageValue);
    }

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

    private KVNestedNodeDefinition GetNestedNodeDefinition(string nestedNodeKey)
    {
        return Definition.NestedNodes.Find(definition => string.Equals(definition.SubSegmentPath, nestedNodeKey, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Nested node '{nestedNodeKey}' is not declared under '{Definition.SubSegmentPath}'.");
    }

    internal KVModel GetNestedNodeModel(string nestedNodeKey)
    {
        return Model.EnsureChildModel(nestedNodeKey);
    }

    private void DetachActiveNestedNode(string nestedNodeKey)
    {
        if (_activeNestedNodes.Remove(nestedNodeKey, out var activeNode))
        {
            activeNode.Node.DetachRuntime();
        }
    }

    internal void DetachNestedNodeForPatch(string nestedNodeKey)
    {
        DetachActiveNestedNode(nestedNodeKey);
    }

    private static void ClearNestedNodeModel(KVModel nestedModel)
    {
        var keysToRemove = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in nestedModel.DirectValueKeys())
        {
            if (!string.Equals(key, KVNestedNode.TypeKey, StringComparison.Ordinal))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            nestedModel.Remove(key);
        }

        foreach (var childKey in new List<string>(nestedModel.ChildModels.Keys))
        {
            nestedModel.MarkChildRemoved(childKey);
        }
    }

    internal void ClearNestedNodeModelForPatch(KVModel nestedModel)
    {
        ClearNestedNodeModel(nestedModel);
    }

    // Returns the path segment relative to this node's model that corresponds to the given canonical path.
    // Used by validation to resolve a field/collection key from a potentially absolute canonical path.
    internal string ResolveStoragePath(string fieldKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        return KVPath.NormalizeRelative(fieldKey);
    }

    internal string ResolveStoragePathForCanonicalPath(string canonicalPath, string currentCanonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var normalizedPath = KVPath.Normalize(canonicalPath);
        var normalizedCurrentPath = KVPath.Normalize(currentCanonicalPath);

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(normalizedCurrentPath))
        {
            return normalizedPath;
        }

        if (string.Equals(normalizedPath, normalizedCurrentPath, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (KVPath.IsSameOrDescendant(normalizedPath, normalizedCurrentPath))
        {
            return normalizedPath[(normalizedCurrentPath.Length + 1)..];
        }

        return normalizedPath;
    }

    internal string GetCanonicalPath() => Model?.DataPath ?? string.Empty;

    internal void EmitChange(string canonicalPath, object? oldValue, object? newValue)
    {
        KVChangeReactionRuntime.Emit(this, canonicalPath, oldValue, newValue);
    }

    internal void RebindCurrentContextForPatch()
    {
        Bind(Model, Definition, Parent);
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

    private sealed record ActiveNestedNode(KVNestedNode Node, string TypeToken, KVModel Model);
}
