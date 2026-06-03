using System;
using System.Collections;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

// Non-generic base exposes delta computation without requiring the type parameter.
public abstract class KVCollectionNodeBase : IKVCollectionNode, IKVCollectionRuntime, IKVNodeCanonicalPath
{
    string IKVNodeCanonicalPath.GetCanonicalPath() => GetCanonicalPath();
    internal abstract string GetCanonicalPath();
    internal abstract KVChangeDeltaGroup ComputeDeltas(string collectionPath);
    public abstract IReadOnlyList<string> GetActiveItemIds();

    // Concrete settable properties — subclass sets via protected set.
    public KVModel Model { get; protected set; } = null!;
    public KVCollectionDefinition Definition { get; protected set; } = null!;
    public IKVNode? Parent { get; protected set; }

    public abstract void Bind(KVModel model, KVCollectionDefinition definition, IKVNode? parent = null);

    // IKVCollectionNode.GetById is explicitly implemented; subclasses provide GetByIdCore.
    KVNode? IKVCollectionNode.GetById(string itemId) => GetByIdCore(itemId);
    protected abstract KVNode? GetByIdCore(string itemId);

    public abstract KVNode Create(Guid itemId, string? typeToken = null);
    public abstract bool RemoveById(string itemId);
    public abstract bool MoveById(string itemId, int toIndex);

    // IKVCollectionRuntime
    void IKVCollectionRuntime.Rebind() => Rebind();
    protected abstract void Rebind();
}

public class KVCollectionNode<TItem> : KVCollectionNodeBase, IEnumerable<TItem>
    where TItem : KVCollectionItemNode, new()
{
    private readonly Dictionary<string, TItem> _items = new(StringComparer.Ordinal);
    private readonly List<string> _orderedItemIds = [];

    // Tracks snapshot-backed items removed in the current draft (for delta computation).
    private readonly HashSet<string> _removedItemIds = new(StringComparer.Ordinal);

    protected bool IsBound => Model is not null && Definition is not null;

    internal override string GetCanonicalPath() => Model?.DataPath ?? string.Empty;

    public override void Bind(KVModel model, KVCollectionDefinition definition, IKVNode? parent = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);

        Parent = parent;
        Model = model;
        Definition = definition;

        _items.Clear();
        _orderedItemIds.Clear();
        _removedItemIds.Clear();

        foreach (var itemId in model.DirectChildKeys())
        {
            var itemDataPath = KVPath.Combine(model.DataPath, itemId);
            if (model.Overlay.IsRemoved(itemDataPath)) continue;

            var itemModel = model.CreateChildModel(itemId);
            KVCollectionItemNode.SetItemId(itemModel, itemId);
            var itemDefinition = ResolveItemDefinition(itemModel, itemId);
            var item = (TItem)Activator.CreateInstance(itemDefinition.ModelType)!;
            item.BindRuntime(itemModel, itemDefinition.NodeDefinition, this);

            _items[itemId] = item;
            _orderedItemIds.Add(itemId);
        }
    }

    public TItem Create() => Create(Guid.NewGuid());

    public TItem Create(Guid itemId) => (TItem)CreateCore(Definition.GetItemDefinition(typeof(TItem)), NormalizeItemId(itemId));

    public TSubtype Create<TSubtype>() where TSubtype : TItem, new() => Create<TSubtype>(Guid.NewGuid());

    public TSubtype Create<TSubtype>(Guid itemId) where TSubtype : TItem, new() => (TSubtype)CreateCore(Definition.GetItemDefinition(typeof(TSubtype)), NormalizeItemId(itemId));

    public override KVNode Create(Guid itemId, string? typeToken = null)
    {
        var itemDefinition = string.IsNullOrWhiteSpace(typeToken) ? Definition.GetItemDefinition(typeof(TItem)) : Definition.GetItemDefinition(typeToken);
        return CreateCore(itemDefinition, NormalizeItemId(itemId));
    }

    private KVNode CreateCore(KVCollectionItemDefinition itemDefinition, string itemId)
    {
        if (!IsValidItemId(itemId))
        {
            throw new InvalidOperationException("Collection item id cannot be empty or contain '/'.");
        }

        if (_items.ContainsKey(itemId))
        {
            throw new InvalidOperationException($"Collection item id '{itemId}' already exists.");
        }

        var item = (TItem)Activator.CreateInstance(itemDefinition.ModelType)!;
        // Restore the path in case it was previously removed (re-adding a deleted item).
        var itemDataPath = KVPath.Combine(Model.DataPath, itemId);
        Model.Overlay.RestorePath(itemDataPath);
        _removedItemIds.Remove(itemId);

        var childModel = Model.CreateChildModel(itemId);
        KVCollectionItemNode.SetItemId(childModel, itemId);
        KVCollectionItemNode.SetItemType(childModel, itemDefinition.TypeToken);

        item.BindRuntime(childModel, itemDefinition.NodeDefinition, this);

        _items[itemId] = item;
        _orderedItemIds.Add(itemId);
        EmitCollectionChange(itemId, oldValue: null, newValue: item);
        return item;
    }

    private KVCollectionItemDefinition ResolveItemDefinition(KVModel itemModel, string itemId)
    {
        var itemType = KVCollectionItemNode.GetItemType(itemModel);
        if (!string.IsNullOrWhiteSpace(itemType))
        {
            return Definition.GetItemDefinition(itemType);
        }

        if (Definition.ItemDefinitionsByType.Count == 1)
        {
            foreach (var definition in Definition.ItemDefinitionsByType.Values)
            {
                KVCollectionItemNode.SetItemType(itemModel, definition.TypeToken);
                return definition;
            }
        }

        throw new InvalidOperationException($"Collection item '{itemId}' does not declare an item type token.");
    }

    private static bool IsValidItemId(string? itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && !itemId.Contains('/');
    }

    public TItem? GetById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        var itemDataPath = KVPath.Combine(Model.DataPath, itemId);
        if (Model.Overlay.IsRemoved(itemDataPath)) return null;
        return _items.GetValueOrDefault(itemId);
    }

    protected override KVNode? GetByIdCore(string itemId) => GetById(itemId);

    public string GetItemId(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var pair in _items)
        {
            if (ReferenceEquals(pair.Value, item))
            {
                return pair.Key;
            }
        }

        throw new InvalidOperationException("Item is not tracked by this collection.");
    }

    public override bool RemoveById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return false;

        if (!_items.Remove(itemId, out var item)) return false;

        _orderedItemIds.Remove(itemId);
        var itemDataPath = KVPath.Combine(Model.DataPath, itemId);
        Model.Overlay.Remove(itemDataPath);

        // Track for delta computation only if it was snapshot-backed.
        if (Model.Overlay.IsSnapshotBacked(itemDataPath))
        {
            _removedItemIds.Add(itemId);
        }

        EmitCollectionChange(itemId, oldValue: itemId, newValue: null);
        return true;
    }

    public override bool MoveById(string itemId, int toIndex)
    {
        if (!_items.ContainsKey(itemId)) return false;

        if (toIndex < 0 || toIndex >= _orderedItemIds.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex));

        var currentIndex = _orderedItemIds.IndexOf(itemId);
        if (currentIndex < 0 || currentIndex == toIndex) return currentIndex >= 0;

        _orderedItemIds.RemoveAt(currentIndex);
        _orderedItemIds.Insert(toIndex, itemId);
        return true;
    }

    public override IReadOnlyList<string> GetActiveItemIds() => _orderedItemIds.AsReadOnly();

    internal override KVChangeDeltaGroup ComputeDeltas(string collectionPath)
    {
        var deltas = new List<KVChangeDelta>();
        var children = new List<KVChangeDeltaGroup>();

        // Removed items (snapshot-backed, deleted in draft).
        foreach (var itemId in _removedItemIds)
        {
            deltas.Add(new KVChangeDelta(KVPath.Combine(collectionPath, itemId), KVChangeDeltaType.Removed));
        }

        // Active items.
        foreach (var (itemId, itemNode) in _items)
        {
            var itemPath = KVPath.Combine(collectionPath, itemId);
            var itemDeltas = itemNode.ComputeDeltas(itemPath, isCollectionItem: true);
            if (itemDeltas.Deltas.Count > 0 || itemDeltas.Children.Count > 0)
                children.Add(itemDeltas);
        }

        return new KVChangeDeltaGroup(deltas, children);
    }

    public virtual IEnumerator<TItem> GetEnumerator()
    {
        if (!IsBound) yield break;

        foreach (var itemId in _orderedItemIds)
        {
            if (_items.TryGetValue(itemId, out var collectionItem))
                yield return collectionItem;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    protected override void Rebind() => Bind(Model, Definition, Parent);

    private static string NormalizeItemId(Guid itemId)
    {
        if (itemId == Guid.Empty)
            throw new ArgumentException("Collection item id cannot be empty.", nameof(itemId));
        return itemId.ToString("D");
    }

    private void EmitCollectionChange(string itemId, object? oldValue, object? newValue)
    {
        if (Parent is not KVNode parentNode) return;
        parentNode.EmitChange(KVPath.Combine(Model.DataPath, itemId), oldValue, newValue);
    }
}
