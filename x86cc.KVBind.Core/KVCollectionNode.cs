using System;
using System.Collections;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

// Non-generic base exposes delta computation without requiring the type parameter.
public abstract class KVCollectionNodeBase : KVNodeBase, IKVCollectionNode
{
    // Concrete settable properties — subclass sets via protected set.
    public KVModel Model { get; protected set; } = null!;

    public KVCollectionDefinition Definition { get; protected set; } = null!;

    public KVNodeBase? Parent { get; protected set; }

    public abstract IReadOnlyList<string> GetActiveItemIds();

    public abstract void Bind(KVModel model, KVCollectionDefinition definition, KVNodeBase? parent = null);

    // IKVCollectionNode.GetById is explicitly implemented; subclasses provide GetByIdCore.
    KVNode? IKVCollectionNode.GetById(string itemId) => this.GetByIdCore(itemId);

    public abstract KVNode Create(string itemId, string? typeToken = null);

    public KVNode Create(Guid itemId, string? typeToken = null) => this.Create(KVCollectionItemIds.FromGuid(itemId), typeToken);

    public abstract bool RemoveById(string itemId);

    public abstract bool MoveById(string itemId, int toIndex);

    internal abstract KVChangeDeltaGroup ComputeDeltas(string collectionPath);

    protected abstract KVNode? GetByIdCore(string itemId);

    protected abstract void Rebind();
}

public class KVCollectionNode<TItem> : KVCollectionNodeBase, IEnumerable<TItem>
    where TItem : KVCollectionItemNode, new()
{
    private const string ItemsKey = "$items";

    private readonly Dictionary<string, TItem> items = new(StringComparer.Ordinal);

    private readonly List<string> orderedItemIds = [];

    protected bool IsBound => this.Model is not null && this.Definition is not null;

    private string ItemsPath => KVPath.Combine(this.Model.DataPath, ItemsKey);

    public override void Bind(KVModel model, KVCollectionDefinition definition, KVNodeBase? parent = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(definition);

        this.Parent = parent;
        this.Model = model;
        this.Definition = definition;

        this.items.Clear();
        this.orderedItemIds.Clear();

        foreach (var itemId in ResolveItemIds(model))
        {
            var itemModel = model.CreateChildModel(itemId);
            var itemDefinition = this.ResolveItemDefinition(itemModel, itemId);
            var item = (TItem)Activator.CreateInstance(itemDefinition.ModelType)!;
            item.BindRuntime(itemModel, itemDefinition.NodeDefinition, this);

            this.items[itemId] = item;
            this.orderedItemIds.Add(itemId);
        }
    }

    public TItem Create() => this.Create(Guid.NewGuid());

    public TItem Create(Guid itemId) => this.Create(KVCollectionItemIds.FromGuid(itemId));

    public TItem Create(string itemId) => (TItem)this.CreateCore(this.Definition.GetItemDefinition(typeof(TItem)), itemId);

    public TSubtype Create<TSubtype>()
        where TSubtype : TItem, new()
        => this.Create<TSubtype>(Guid.NewGuid());

    public TSubtype Create<TSubtype>(Guid itemId)
        where TSubtype : TItem, new()
        => this.Create<TSubtype>(KVCollectionItemIds.FromGuid(itemId));

    public TSubtype Create<TSubtype>(string itemId)
        where TSubtype : TItem, new()
        => (TSubtype)this.CreateCore(this.Definition.GetItemDefinition(typeof(TSubtype)), itemId);

    public override KVNode Create(string itemId, string? typeToken = null)
    {
        var itemDefinition = string.IsNullOrWhiteSpace(typeToken) ? this.Definition.GetItemDefinition(typeof(TItem)) : this.Definition.GetItemDefinition(typeToken);
        return this.CreateCore(itemDefinition, itemId);
    }

    // Typed lookup for polymorphic collections: null when the id is absent, throws when the stored item is
    // a different subtype.
    public TSubtype? Get<TSubtype>(string itemId)
        where TSubtype : TItem
    {
        var item = this.GetById(itemId);
        return item switch
        {
            null => null,
            TSubtype typed => typed,
            _ => throw new InvalidOperationException($"Collection item '{itemId}' is '{item.GetType().FullName}', not '{typeof(TSubtype).FullName}'.")
        };
    }

    public TItem? GetById(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        var itemDataPath = KVPath.Combine(this.Model.DataPath, itemId);
        if (this.Model.Overlay.IsRemoved(itemDataPath)) return null;
        return this.items.GetValueOrDefault(itemId);
    }

    public string GetItemId(TItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var pair in this.items)
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

        if (!this.items.Remove(itemId, out _)) return false;

        this.orderedItemIds.Remove(itemId);
        this.Model.Overlay.Remove(KVPath.Combine(this.Model.DataPath, itemId));
        this.WriteItemsKey();
        this.EmitCollectionChange(itemId, oldValue: itemId, newValue: null, KVChangeKind.Removed);
        return true;
    }

    public override bool MoveById(string itemId, int toIndex)
    {
        if (!this.items.ContainsKey(itemId)) return false;

        if (toIndex < 0 || toIndex >= this.orderedItemIds.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex));

        var currentIndex = this.orderedItemIds.IndexOf(itemId);
        if (currentIndex < 0 || currentIndex == toIndex) return currentIndex >= 0;

        this.orderedItemIds.RemoveAt(currentIndex);
        this.orderedItemIds.Insert(toIndex, itemId);
        this.WriteItemsKey();
        this.EmitCollectionChange(itemId, oldValue: currentIndex, newValue: toIndex, KVChangeKind.Moved);
        return true;
    }

    public override IReadOnlyList<string> GetActiveItemIds() => this.orderedItemIds.AsReadOnly();

    public virtual IEnumerator<TItem> GetEnumerator()
    {
        if (!this.IsBound) yield break;

        foreach (var itemId in this.orderedItemIds)
        {
            if (this.items.TryGetValue(itemId, out var collectionItem))
                yield return collectionItem;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    internal override string GetCanonicalPath() => this.Model?.DataPath ?? string.Empty;

    internal override KVChangeDeltaGroup ComputeDeltas(string collectionPath)
    {
        var deltas = new List<KVChangeDelta>();
        var children = new List<KVChangeDeltaGroup>();

        // Build snapshot item set for comparison.
        string[] snapIds = [];
        if (this.Model.Overlay.TryGetSnapshotValue(this.ItemsPath, out var snapVal) && snapVal?.Value is string[] s)
            snapIds = s;
        var snapSet = new HashSet<string>(snapIds, StringComparer.Ordinal);
        var draftSet = new HashSet<string>(this.orderedItemIds, StringComparer.Ordinal);

        // Items in snapshot but not in draft → removed.
        foreach (var id in snapIds)
        {
            if (!draftSet.Contains(id))
                deltas.Add(new KVChangeDelta(KVPath.Combine(collectionPath, id), KVChangeDeltaType.Removed));
        }

        // Active items — pass isNew so new items short-circuit to Added.
        foreach (var (itemId, itemNode) in this.items)
        {
            var itemPath = KVPath.Combine(collectionPath, itemId);
            var isNew = !snapSet.Contains(itemId);
            var itemDeltas = itemNode.ComputeDeltas(itemPath, isCollectionItem: isNew);
            if (itemDeltas.Deltas.Count > 0 || itemDeltas.Children.Count > 0)
                children.Add(itemDeltas);
        }

        return new KVChangeDeltaGroup(deltas, children);
    }

    protected override KVNode? GetByIdCore(string itemId) => this.GetById(itemId);

    protected override void Rebind() => this.Bind(this.Model, this.Definition, this.Parent);

    private static IEnumerable<string> ResolveItemIds(KVModel model)
    {
        var itemsPath = KVPath.Combine(model.DataPath, ItemsKey);
        if (model.Overlay.TryGet(itemsPath, out var val) && val?.Value is string[] ids)
            return ids;
        return [];
    }

    private KVNode CreateCore(KVCollectionItemDefinition itemDefinition, string itemId)
    {
        KVCollectionItemIds.EnsureValid(itemId);

        if (this.items.ContainsKey(itemId))
        {
            throw new InvalidOperationException($"Collection item id '{itemId}' already exists.");
        }

        var item = (TItem)Activator.CreateInstance(itemDefinition.ModelType)!;

        // Re-adding a previously removed id must yield a fresh item: lift the tombstone without letting the
        // old snapshot data underneath it resurface.
        var itemDataPath = KVPath.Combine(this.Model.DataPath, itemId);
        this.Model.Overlay.ReviveRemovedPath(itemDataPath);

        var childModel = this.Model.CreateChildModel(itemId);
        KVCollectionItemNode.SetItemType(childModel, itemDefinition.TypeToken);

        item.BindRuntime(childModel, itemDefinition.NodeDefinition, this);

        this.items[itemId] = item;
        this.orderedItemIds.Add(itemId);
        this.WriteItemsKey();

        // A new item is born here (code, ADD patch, seeding): materialize its declared defaults — field
        // defaults, default nested types, seeded sub-collections — before anyone observes it.
        item.ApplyDefaultsRecursive();
        this.EmitCollectionChange(itemId, oldValue: null, newValue: item, KVChangeKind.Added);
        return item;
    }

    private KVCollectionItemDefinition ResolveItemDefinition(KVModel itemModel, string itemId)
    {
        var itemType = KVCollectionItemNode.GetItemType(itemModel);
        if (!string.IsNullOrWhiteSpace(itemType))
        {
            return this.Definition.GetItemDefinition(itemType);
        }

        if (this.Definition.ItemDefinitionsByType.Count == 1)
        {
            foreach (var definition in this.Definition.ItemDefinitionsByType.Values)
            {
                KVCollectionItemNode.SetItemType(itemModel, definition.TypeToken);
                return definition;
            }
        }

        throw new InvalidOperationException($"Collection item '{itemId}' does not declare an item type token.");
    }

    private void WriteItemsKey()
    {
        this.Model.Overlay.Set(this.ItemsPath, new KVValue<string[]>(this.orderedItemIds.ToArray()));
    }

    private void EmitCollectionChange(string itemId, object? oldValue, object? newValue, KVChangeKind kind)
    {
        if (this.Parent is not KVNode parentNode) return;
        parentNode.EmitChange(KVPath.Combine(this.Model.DataPath, itemId), oldValue, newValue, kind);
    }
}
