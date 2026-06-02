using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public class KVModel
{
    private const string InternalIdKey = "$id";
    private const string InternalTypeKey = "$type";

    public Dictionary<string, KVModel> ChildModels { get; } = new(StringComparer.Ordinal);

    private readonly HashSet<string> _removedChildModels = new(StringComparer.Ordinal);

    public KVOverlay Overlay { get; private set; }

    internal string DataPath { get; }

    public KVModel()
        : this(KVOverlay.Create(new KVSnapshot(), "system"), string.Empty)
    {
    }

    public KVModel(KVOverlay overlay)
        : this(overlay, string.Empty)
    {
    }

    internal KVModel(KVOverlay overlay, string dataPath)
    {
        Overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        DataPath = KVPath.Normalize(dataPath);
    }

    public TValue Get<TValue>(string segment)
    {
        if (ChildModels.ContainsKey(segment)) throw new InvalidOperationException("Child store access");

        Overlay.TryGet(ResolveDataPath(segment), out var value);
        return (TValue)value!;
    }

    public void Set<TValue>(string segment, TValue value)
    {
        if (ChildModels.ContainsKey(segment)) throw new InvalidOperationException("Child store access");

        Overlay.Set(ResolveDataPath(segment), value);
    }

    public bool Remove(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return Overlay.Remove(ResolveDataPath(segment));
    }

    public KVModel EnsureChildModel(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

        if (!ChildModels.TryGetValue(key, out var child))
        {
            child = new KVModel(Overlay, ResolveDataPath(key));
            ChildModels[key] = child;
        }

        _removedChildModels.Remove(key);
        return child;
    }

    public KVCollectionModel EnsureCollectionModel(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

        if (!ChildModels.TryGetValue(key, out var child))
        {
            var collection = new KVCollectionModel(Overlay, ResolveDataPath(key));
            ChildModels[key] = collection;
            _removedChildModels.Remove(key);
            return collection;
        }

        _removedChildModels.Remove(key);

        return child as KVCollectionModel
               ?? throw new InvalidOperationException($"Child model '{key}' is not a collection model.");
    }

    public void MarkChildRemoved(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

        if (!ChildModels.TryGetValue(key, out var child))
        {
            return;
        }

        Overlay.Remove(child.DataPath);

        if (!child.IsSnapshotBacked())
        {
            ChildModels.Remove(key);
            _removedChildModels.Remove(key);
            return;
        }

        _removedChildModels.Add(key);
    }

    public void UnmarkChildRemoved(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);
        _removedChildModels.Remove(key);
        Overlay.RestorePath(ResolveDataPath(key));
    }

    public bool IsChildRemoved(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);
        if (_removedChildModels.Contains(key))
        {
            return true;
        }

        return Overlay.IsRemoved(ResolveDataPath(key));
    }

    public void ReplaceOverlay(KVOverlay overlay)
    {
        Overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        foreach (var child in ChildModels.Values)
        {
            child.ReplaceOverlay(overlay);
        }
    }

    internal IReadOnlyList<string> DirectValueKeys()
    {
        var keys = new List<string>();
        foreach (var segment in Overlay.DirectKeys(DataPath, ChildModels.ContainsKey))
        {
            keys.Add(segment);
        }

        return keys;
    }

    internal void PruneDraftChildren(string discardedPath)
    {
        foreach (var child in new List<KeyValuePair<string, KVModel>>(ChildModels))
        {
            child.Value.PruneDraftChildren(discardedPath);
            if (this is KVCollectionModel
                && KVPath.IsSameOrDescendant(child.Value.DataPath, discardedPath)
                && !child.Value.IsSnapshotBacked())
            {
                ChildModels.Remove(child.Key);
            }
        }
    }

    internal void ClearRemovedChildMarks(string discardedPath)
    {
        foreach (var removedChild in new List<string>(_removedChildModels))
        {
            var childPath = KVPath.Combine(DataPath, removedChild);
            if (KVPath.IsSameOrDescendant(childPath, discardedPath))
            {
                _removedChildModels.Remove(removedChild);
            }
        }

        foreach (var child in ChildModels.Values)
        {
            child.ClearRemovedChildMarks(discardedPath);
        }
    }

    internal KVChangeDeltaGroup ComputeNodeDeltas(string nodePath, bool isCollectionItem)
    {
        var deltas = new List<KVChangeDelta>();
        if (true)
        {
            if (!isCollectionItem && !string.IsNullOrWhiteSpace(nodePath) && TryCreateNestedNodeTypeDelta(nodePath, Overlay, out var nestedNodeTypeDelta))
            {
                return new KVChangeDeltaGroup([nestedNodeTypeDelta], []);
            }

            if (isCollectionItem && !IsSnapshotBacked() && HasDraftState())
            {
                return new KVChangeDeltaGroup([new KVChangeDelta(nodePath, KVChangeDeltaType.Added)], []);
            }

            foreach (var pair in Overlay.DirectDraftValues(DataPath, ChildModels.ContainsKey))
            {
                var key = pair.Key;
                if (IsInternalMetadataKey(key))
                {
                    continue;
                }

                var dataPath = ResolveDataPath(key);
                var hasSnapshot = Overlay.TryGetSnapshotValue(dataPath, out var snapshotValue);
                if (!hasSnapshot)
                {
                    deltas.Add(new KVChangeDelta(KVPath.Combine(nodePath, key), KVChangeDeltaType.Added));
                    continue;
                }

                if (!Equals(snapshotValue, pair.Value))
                {
                    deltas.Add(new KVChangeDelta(KVPath.Combine(nodePath, key), KVChangeDeltaType.Updated));
                }
            }

            foreach (var removed in Overlay.DirectRemovedValues(DataPath, ChildModels.ContainsKey))
            {
                if (IsInternalMetadataKey(removed))
                {
                    continue;
                }

                var dataPath = ResolveDataPath(removed);
                if (Overlay.IsSnapshotBacked(dataPath))
                {
                    deltas.Add(new KVChangeDelta(KVPath.Combine(nodePath, removed), KVChangeDeltaType.Removed));
                }
            }
        }

        var children = new List<KVChangeDeltaGroup>();

        foreach (var childKey in _removedChildModels)
        {
            if (!ChildModels.TryGetValue(childKey, out var childModel) || !childModel.IsSnapshotBacked())
            {
                continue;
            }

            deltas.Add(new KVChangeDelta(KVPath.Combine(nodePath, childKey), KVChangeDeltaType.Removed));
        }

        foreach (var child in ChildModels)
        {
            if (_removedChildModels.Contains(child.Key))
            {
                continue;
            }

            var childPath = KVPath.Combine(nodePath, child.Key);
            var childDeltas = child.Value.ComputeNodeDeltas(childPath, this is KVCollectionModel);
            if (childDeltas.Deltas.Count == 0 && childDeltas.Children.Count == 0)
            {
                continue;
            }

            children.Add(childDeltas);
        }

        return new KVChangeDeltaGroup(deltas, children);
    }

    private string ResolveDataPath(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return KVPath.Combine(DataPath, KVPath.Normalize(segment));
    }

    private bool IsSnapshotBacked()
    {
        return Overlay.IsSnapshotBacked(DataPath);
    }

    private bool HasDraftState()
    {
        if (Overlay.HasDraftState(DataPath))
        {
            return true;
        }

        foreach (var child in ChildModels.Values)
        {
            if (child.HasDraftState())
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateNestedNodeTypeDelta(string nodePath, KVOverlay overlay, out KVChangeDelta delta)
    {
        delta = default!;
        var typePath = KVPath.Combine(nodePath, InternalTypeKey);
        if (overlay.HasRemovedPath(typePath))
        {
            if (!overlay.TryGetSnapshotValue(typePath, out _))
            {
                return false;
            }

            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Removed);
            return true;
        }

        if (!overlay.TryGetDraftValue(typePath, out var overlayValue))
        {
            return false;
        }

        var hasSnapshot = overlay.TryGetSnapshotValue(typePath, out var snapshotValue);
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

    private static bool IsInternalMetadataKey(string key)
    {
        return string.Equals(key, InternalIdKey, StringComparison.Ordinal)
               || string.Equals(key, InternalTypeKey, StringComparison.Ordinal);
    }

}
