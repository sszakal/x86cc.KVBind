using System;
using System.Collections;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public class KVCollectionNode<TItem> : IEnumerable<TItem>, IKVCollectionNode, IKVCollectionRuntime
    where TItem : KVCollectionItemNode, new()
{
    private readonly Dictionary<string, TItem> _items = new(StringComparer.Ordinal);
    private readonly List<string> _orderedItemIds = [];

    internal string StoragePath { get; private set; } = string.Empty;

    public IKVNode? Parent { get; private set; }
    public KVCollectionModel Model { get; private set; } = null!;
    public KVCollectionDefinition Definition { get; private set; } = null!;

    protected bool IsBound => Model is not null && Definition is not null;

    public void Bind(KVCollectionModel model, KVCollectionDefinition definition, IKVNode? parent = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);

        Parent = parent;
        Model = model;
        Definition = definition;
        StoragePath = parent is KVNode parentNode ? KVPath.Combine(parentNode.StoragePath, definition.SubSegmentPath) : definition.SubSegmentPath;

        _items.Clear();
        _orderedItemIds.Clear();

        foreach (var pair in model.ChildModels)
        {
            var itemId = pair.Key;
            if (string.IsNullOrWhiteSpace(itemId) || model.IsChildRemoved(itemId))
            {
                continue;
            }

            var itemModel = pair.Value;
            KVCollectionItemNode.SetItemId(itemModel, itemId);
            var itemDefinition = ResolveItemDefinition(itemModel, itemId);
            var item = (TItem)Activator.CreateInstance(itemDefinition.ModelType)!;
            item.BindRuntime(itemModel, itemDefinition.NodeDefinition, this, itemId, storagePathOverride: string.Empty);

            _items[itemId] = item;
            _orderedItemIds.Add(itemId);
        }
    }

    public TItem Create() => Create(Guid.NewGuid());

    public TItem Create(Guid itemId) => (TItem)CreateCore(Definition.GetItemDefinition(typeof(TItem)), NormalizeItemId(itemId));

    public TSubtype Create<TSubtype>() where TSubtype : TItem, new() => Create<TSubtype>(Guid.NewGuid());

    public TSubtype Create<TSubtype>(Guid itemId) where TSubtype : TItem, new() => (TSubtype)CreateCore(Definition.GetItemDefinition(typeof(TSubtype)), NormalizeItemId(itemId));

    public virtual KVNode Create(Guid itemId, string? typeToken = null)
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
        var childModel = Model.EnsureItemModel(itemId);
        KVCollectionItemNode.SetItemId(childModel, itemId);
        KVCollectionItemNode.SetItemType(childModel, itemDefinition.TypeToken);

        item.BindRuntime(childModel, itemDefinition.NodeDefinition, this, itemId, storagePathOverride: string.Empty);

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
        if (string.IsNullOrWhiteSpace(itemId) || Model.IsChildRemoved(itemId))
        {
            return null;
        }

        return _items.GetValueOrDefault(itemId);
    }

    KVNode? IKVCollectionNode.GetById(string itemId) => GetById(itemId);

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

    public virtual bool RemoveById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        var removed = _items.Remove(itemId);
        if (!removed)
        {
            return false;
        }

        _orderedItemIds.Remove(itemId);
        Model.MarkChildRemoved(itemId);
        EmitCollectionChange(itemId, oldValue: itemId, newValue: null);
        return true;
    }

    public virtual bool MoveById(string itemId, int toIndex)
    {
        if (!_items.ContainsKey(itemId))
        {
            return false;
        }

        if (toIndex < 0 || toIndex >= _orderedItemIds.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(toIndex));
        }

        var currentIndex = _orderedItemIds.IndexOf(itemId);
        if (currentIndex < 0 || currentIndex == toIndex)
        {
            return currentIndex >= 0;
        }

        _orderedItemIds.RemoveAt(currentIndex);
        _orderedItemIds.Insert(toIndex, itemId);
        return true;
    }

    public virtual IEnumerator<TItem> GetEnumerator()
    {
        if (!IsBound)
        {
            yield break;
        }

        foreach (var itemId in _orderedItemIds)
        {
            if (_items.TryGetValue(itemId, out var collectionItem))
            {
                yield return collectionItem;
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void IKVCollectionRuntime.Rebind()
    {
        Bind(Model, Definition, Parent);
    }

    private static string NormalizeItemId(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            throw new ArgumentException("Collection item id cannot be empty.", nameof(itemId));
        }

        return itemId.ToString("D");
    }

    private void EmitCollectionChange(string itemId, object? oldValue, object? newValue)
    {
        if (Parent is not KVNode parentNode)
        {
            return;
        }

        var collectionPath = KVPath.Combine(parentNode.GetCanonicalPath(), Definition.SubSegmentPath);
        parentNode.EmitChange(KVPath.Combine(collectionPath, itemId), oldValue, newValue);
    }

}
