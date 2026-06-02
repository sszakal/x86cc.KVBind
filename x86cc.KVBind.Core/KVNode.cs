using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract class KVNode: IKVNode
{
    private readonly Dictionary<string, ActiveNestedNode> _activeNestedNodes = new(StringComparer.Ordinal);
    private bool _isDetached;

    public IKVNode? Parent { get; private set; }
    public KVModel Model { get; private set; } = null!;
    public KVNodeDefinition Definition { get; private set; } = null!;
    internal string StoragePath { get; private set; } = string.Empty;
    private string? _boundSubSegmentPath;
    private bool IsBound => Model is not null && Definition is not null;
    
    
    internal void BindRuntime(
        KVModel model,
        KVNodeDefinition definition,
        IKVNode? parent = null,
        string? subSegmentOverride = null,
        string? storagePathOverride = null)
    {
        Bind(model, definition, parent, subSegmentOverride, storagePathOverride);
    }

    protected virtual void Bind(
        KVModel model,
        KVNodeDefinition definition,
        IKVNode? parent = null,
        string? subSegmentOverride = null,
        string? storagePathOverride = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);
        
        var pathSegment = subSegmentOverride ?? definition.SubSegmentPath;
        if (parent is not null && string.IsNullOrWhiteSpace(pathSegment))
        {
            throw new InvalidOperationException("Non-root node definitions must define SubSegmentPath.");
        }
        
        if (!_isDetached && IsBound && (!ReferenceEquals(Parent, parent) || !ReferenceEquals(Definition, definition)))
        {
            throw new InvalidOperationException("KVNode is already bound to a different context.");
        }

        DetachActiveNestedNodes();
        
        _isDetached = false;
        Parent = parent;
        Model = model;
        Definition = definition;
        _boundSubSegmentPath = pathSegment;
        StoragePath = storagePathOverride ?? ResolveChildStoragePath(parent, pathSegment);
        
        foreach (var nodeDefinition in definition.Nodes)
        {
            var childNode = nodeDefinition.GetChildNode(this)
                            ?? throw new InvalidOperationException($"Node '{nodeDefinition.SubSegmentPath}' resolved to null.");
            childNode.Bind(model, nodeDefinition, this, nodeDefinition.SubSegmentPath);
        }
        
        foreach (var collectionDefinition in definition.Collections)
        {
            if (string.IsNullOrWhiteSpace(collectionDefinition.SubSegmentPath))
            {
                throw new InvalidOperationException("Collection definitions must define SubSegmentPath.");
            }

            var collectionNode = collectionDefinition.GetCollection(this) ?? throw new InvalidOperationException($"Collection '{collectionDefinition.SubSegmentPath}' resolved to null.");
            var collectionPath = CombineStoragePath(StoragePath, collectionDefinition.SubSegmentPath);
            var collectionModel = model.EnsureCollectionModel(collectionPath);
            collectionNode.Bind(collectionModel, collectionDefinition, this);
        }

        foreach (var nestedNodeDefinition in definition.NestedNodes)
        {
            if (string.IsNullOrWhiteSpace(nestedNodeDefinition.SubSegmentPath))
            {
                throw new InvalidOperationException("Nested node definitions must define SubSegmentPath.");
            }

            var nestedPath = CombineStoragePath(StoragePath, nestedNodeDefinition.SubSegmentPath);
            model.EnsureChildModel(nestedPath);
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
        _boundSubSegmentPath = null;
        StoragePath = string.Empty;
    }

    private void DetachActiveNestedNodes()
    {
        foreach (var activeNode in _activeNestedNodes.Values)
        {
            activeNode.Node.DetachRuntime();
        }

        _activeNestedNodes.Clear();
    }
    
    private void EnsureFieldDefined(string subSegmentPath)
    {
        var exists = Definition.Fields.Exists(f => string.Equals(f.SubSegmentPath, subSegmentPath, StringComparison.Ordinal));
        if (exists) return;
        throw new InvalidOperationException($"Field '{subSegmentPath}' is not declared under '{Definition.SubSegmentPath}'.");
    }

    private static string CombineStoragePath(string prefix, string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        return KVPath.Combine(prefix, segment);
    }

    private static string ResolveChildStoragePath(IKVNode? parent, string pathSegment)
    {
        if (parent is null)
        {
            return string.Empty;
        }

        return parent is KVNode parentNode
            ? CombineStoragePath(parentNode.StoragePath, pathSegment)
            : string.Empty;
    }
    
    protected TValue GetField<TValue>(string fieldKey)
    {
        EnsureBound();
        ArgumentNullException.ThrowIfNull(fieldKey);
        EnsureFieldDefined(fieldKey);
        var resolvedPath = ResolveStoragePath(fieldKey);
        return Model.Get<TValue>(resolvedPath);
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
        var resolvedPath = ResolveStoragePath(fieldKey);
        var oldValue = Model.Get<object?>(resolvedPath);
        if (!Model.Remove(resolvedPath))
        {
            return;
        }

        EmitChange(BuildPath(GetCanonicalPath(), fieldKey), oldValue, newValue: null);
    }

    private void SetFieldCore(string fieldKey, object? value)
    {
        var resolvedPath = ResolveStoragePath(fieldKey);
        var oldValue = Model.Get<object?>(resolvedPath);
        if (Equals(oldValue, value))
        {
            return;
        }

        Model.Set(resolvedPath, value);
        EmitChange(BuildPath(GetCanonicalPath(), fieldKey), oldValue, value);
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
        node.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this, nestedNodeKey, storagePathOverride: string.Empty);
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
        value.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this, nestedNodeKey, storagePathOverride: string.Empty);
        _activeNestedNodes[nestedNodeKey] = new ActiveNestedNode(value, typeDefinition.TypeToken, nestedModel);
    }

    private KVNestedNodeDefinition GetNestedNodeDefinition(string nestedNodeKey)
    {
        return Definition.NestedNodes.Find(definition => string.Equals(definition.SubSegmentPath, nestedNodeKey, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Nested node '{nestedNodeKey}' is not declared under '{Definition.SubSegmentPath}'.");
    }

    internal KVModel GetNestedNodeModel(string nestedNodeKey)
    {
        var storagePath = ResolveStoragePath(nestedNodeKey);
        return Model.EnsureChildModel(storagePath);
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

    internal string ResolveStoragePath(string fieldKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        return CombineStoragePath(StoragePath, KVPath.NormalizeRelative(fieldKey));
    }

    internal string ResolveStoragePathForCanonicalPath(string canonicalPath, string currentCanonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var normalizedPath = KVPath.Normalize(canonicalPath);
        var normalizedCurrentPath = KVPath.Normalize(currentCanonicalPath);

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return StoragePath;
        }

        if (string.IsNullOrWhiteSpace(normalizedCurrentPath))
        {
            return CombineStoragePath(StoragePath, normalizedPath);
        }

        if (string.Equals(normalizedPath, normalizedCurrentPath, StringComparison.Ordinal))
        {
            return StoragePath;
        }

        if (KVPath.IsSameOrDescendant(normalizedPath, normalizedCurrentPath))
        {
            var relativePath = normalizedPath[(normalizedCurrentPath.Length + 1)..];
            return CombineStoragePath(StoragePath, relativePath);
        }

        return CombineStoragePath(StoragePath, normalizedPath);
    }

    internal string GetCanonicalPath()
    {
        if (Parent is null)
        {
            return string.Empty;
        }

        if (Parent is KVNode parentNode)
        {
            return BuildPath(parentNode.GetCanonicalPath(), _boundSubSegmentPath ?? Definition.SubSegmentPath);
        }

        if (Parent is IKVCollectionNode collectionNode && collectionNode.Parent is KVNode collectionParent)
        {
            var collectionPath = BuildPath(collectionParent.GetCanonicalPath(), collectionNode.Definition.SubSegmentPath);
            return BuildPath(collectionPath, _boundSubSegmentPath ?? string.Empty);
        }

        return _boundSubSegmentPath ?? Definition.SubSegmentPath;
    }

    internal void EmitChange(string canonicalPath, object? oldValue, object? newValue)
    {
        KVChangeReactionRuntime.Emit(this, canonicalPath, oldValue, newValue);
    }

    internal void RebindCurrentContextForPatch()
    {
        Bind(Model, Definition, Parent, _boundSubSegmentPath, StoragePath);
    }

    private static string BuildPath(string prefix, string segment)
    {
        return KVPath.Combine(prefix, segment);
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
        node.BindRuntime(nestedModel, typeDefinition.NodeDefinition, this, definition.SubSegmentPath, storagePathOverride: string.Empty);
        _activeNestedNodes[definition.SubSegmentPath] = new ActiveNestedNode(node, typeToken, nestedModel);
        return node;
    }

    private sealed record ActiveNestedNode(KVNestedNode Node, string TypeToken, KVModel Model);
}
