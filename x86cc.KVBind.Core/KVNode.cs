using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract class KVNode: IKVNode
{
    private readonly Dictionary<string, ActiveNestedNode> _activeNestedNodes = new(StringComparer.Ordinal);

    // Cached slot models for nested nodes — created at bind time, reused across accesses.
    private readonly Dictionary<string, KVModel> _nestedSlotModels = new(StringComparer.Ordinal);

    private bool _isDetached;

    public IKVNode? Parent { get; private set; }
    public KVModel Model { get; private set; } = null!;
    public KVNodeDefinition Definition { get; protected set; } = null!;
    private bool IsBound => Model is not null && Definition is not null;
    
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
        _nestedSlotModels.Clear();

        _isDetached = false;
        Parent = parent;
        Model = model;
        Definition = definition;

        foreach (var nodeDefinition in definition.Nodes)
        {
            var childNode = nodeDefinition.GetChildNode(this)
                            ?? throw new InvalidOperationException($"Node '{nodeDefinition.SubSegmentPath}' resolved to null.");
            var childModel = model.CreateChildModel(nodeDefinition.SubSegmentPath);
            childNode.Bind(childModel, nodeDefinition, this);
        }

        foreach (var collectionDefinition in definition.Collections)
        {
            if (string.IsNullOrWhiteSpace(collectionDefinition.SubSegmentPath))
            {
                throw new InvalidOperationException("Collection definitions must define SubSegmentPath.");
            }

            var collectionNode = collectionDefinition.GetCollection(this) ?? throw new InvalidOperationException($"Collection '{collectionDefinition.SubSegmentPath}' resolved to null.");
            var collectionModel = model.CreateChildModel(collectionDefinition.SubSegmentPath);
            collectionModel.Overlay.RestorePath(collectionModel.DataPath); // ensure not marked removed
            collectionNode.Bind(collectionModel, collectionDefinition, this);
        }

        foreach (var nestedNodeDefinition in definition.NestedNodes)
        {
            if (string.IsNullOrWhiteSpace(nestedNodeDefinition.SubSegmentPath))
            {
                throw new InvalidOperationException("Nested node definitions must define SubSegmentPath.");
            }

            _nestedSlotModels[nestedNodeDefinition.SubSegmentPath] = model.CreateChildModel(nestedNodeDefinition.SubSegmentPath);
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
        _nestedSlotModels.Clear();
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
        if (_nestedSlotModels.TryGetValue(nestedNodeKey, out var slotModel))
        {
            return slotModel;
        }

        // Lazily create if not pre-populated (e.g. accessed before first full bind).
        slotModel = Model.CreateChildModel(nestedNodeKey);
        _nestedSlotModels[nestedNodeKey] = slotModel;
        return slotModel;
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
        var prefix = nestedModel.DataPath;
        var typeKey = KVPath.Combine(prefix, KVNestedNode.TypeKey);
        var overlay = nestedModel.Overlay;

        // Remove all draft values under this path except $type.
        var toRemove = new List<string>();
        foreach (var key in overlay.AddedOrChanged.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, prefix) && !string.Equals(key, typeKey, StringComparison.Ordinal))
            {
                toRemove.Add(key);
            }
        }
        foreach (var key in toRemove)
        {
            overlay.Remove(key);
        }

        // For snapshot-backed keys, mark them removed too (so they show as deleted in deltas).
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

    internal void ClearNestedNodeModelForPatch(KVModel nestedModel)
    {
        ClearNestedNodeModel(nestedModel);
    }

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

        if (string.IsNullOrWhiteSpace(normalizedPath)) return string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCurrentPath)) return normalizedPath;
        if (string.Equals(normalizedPath, normalizedCurrentPath, StringComparison.Ordinal)) return string.Empty;
        if (KVPath.IsSameOrDescendant(normalizedPath, normalizedCurrentPath))
            return normalizedPath[(normalizedCurrentPath.Length + 1)..];
        return normalizedPath;
    }

    internal string GetCanonicalPath() => Model?.DataPath ?? string.Empty;

    // Compute change deltas for this node and its children.
    internal KVChangeDeltaGroup ComputeDeltas(string nodePath, bool isCollectionItem)
    {
        var deltas = new List<KVChangeDelta>();

        // Nested node type change: emit a single slot-level delta instead of field deltas.
        if (!isCollectionItem && !string.IsNullOrWhiteSpace(nodePath))
        {
            if (TryCreateNestedNodeTypeDelta(Model, nodePath, out var typeDelta))
                return new KVChangeDeltaGroup([typeDelta], []);
        }

        // New (draft-only) collection item.
        if (isCollectionItem && !Model.Overlay.IsSnapshotBacked(Model.DataPath) && HasDraftState())
            return new KVChangeDeltaGroup([new KVChangeDelta(nodePath, KVChangeDeltaType.Added)], []);

        // Field-level changes.
        foreach (var field in Definition.Fields)
        {
            var absPath = KVPath.Combine(Model.DataPath, field.SubSegmentPath);
            var canonPath = KVPath.Combine(nodePath, field.SubSegmentPath);

            if (Model.Overlay.HasRemovedPath(absPath))
            {
                if (Model.Overlay.TryGetSnapshotValue(absPath, out _))
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Removed));
            }
            else if (Model.Overlay.TryGetDraftValue(absPath, out var draftVal))
            {
                var hasSnap = Model.Overlay.TryGetSnapshotValue(absPath, out var snapVal);
                if (!hasSnap)
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Added));
                else if (!Equals(snapVal, draftVal))
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Updated));
            }
        }

        var children = new List<KVChangeDeltaGroup>();

        // Field group children.
        foreach (var childDef in Definition.Nodes)
        {
            var childNode = childDef.GetChildNode(this);
            if (childNode is null) continue;
            var childPath = KVPath.Combine(nodePath, childDef.SubSegmentPath);
            var childDeltas = childNode.ComputeDeltas(childPath, false);
            if (childDeltas.Deltas.Count > 0 || childDeltas.Children.Count > 0)
                children.Add(childDeltas);
        }

        // Collections.
        foreach (var collDef in Definition.Collections)
        {
            var collNode = collDef.GetCollection(this);
            if (collNode is not KVCollectionNodeBase collBase) continue;
            var collPath = KVPath.Combine(nodePath, collDef.SubSegmentPath);
            var collDeltas = collBase.ComputeDeltas(collPath);
            if (collDeltas.Deltas.Count > 0 || collDeltas.Children.Count > 0)
                children.Add(collDeltas);
        }

        // Nested nodes.
        foreach (var nestedDef in Definition.NestedNodes)
        {
            var nestedPath = KVPath.Combine(nodePath, nestedDef.SubSegmentPath);
            var slotModel = GetNestedNodeModel(nestedDef.SubSegmentPath);
            var activeNode = GetActiveNestedNode(nestedDef, slotModel);

            if (activeNode is not null)
            {
                var nestedDeltas = activeNode.ComputeDeltas(nestedPath, false);
                if (nestedDeltas.Deltas.Count > 0 || nestedDeltas.Children.Count > 0)
                    children.Add(nestedDeltas);
            }
            else
            {
                // No active node — check for removal.
                var typeAbsPath = KVPath.Combine(slotModel.DataPath, KVNestedNode.TypeKey);
                if (Model.Overlay.HasRemovedPath(typeAbsPath) && Model.Overlay.TryGetSnapshotValue(typeAbsPath, out _))
                    deltas.Add(new KVChangeDelta(nestedPath, KVChangeDeltaType.Removed));
            }
        }

        return new KVChangeDeltaGroup(deltas, children);
    }

    private bool HasDraftState()
    {
        if (Model.Overlay.HasDraftState(Model.DataPath)) return true;
        foreach (var childDef in Definition.Nodes)
        {
            var childNode = childDef.GetChildNode(this);
            if (childNode is KVNode kvChild && kvChild.HasDraftState()) return true;
        }
        return false;
    }

    private static bool TryCreateNestedNodeTypeDelta(KVModel model, string nodePath, out KVChangeDelta delta)
    {
        delta = default!;
        var typePath = KVPath.Combine(nodePath, KVNestedNode.TypeKey);

        if (model.Overlay.HasRemovedPath(typePath))
        {
            if (!model.Overlay.TryGetSnapshotValue(typePath, out _)) return false;
            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Removed);
            return true;
        }

        if (!model.Overlay.TryGetDraftValue(typePath, out var overlayValue)) return false;

        var hasSnapshot = model.Overlay.TryGetSnapshotValue(typePath, out var snapshotValue);
        if (!hasSnapshot)
        {
            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Added);
            return true;
        }

        if (!Equals(snapshotValue, overlayValue))
        {
            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Updated);
            return true;
        }

        return false;
    }

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
