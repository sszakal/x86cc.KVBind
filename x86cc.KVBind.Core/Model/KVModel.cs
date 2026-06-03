using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public class KVModel
{
    private const string InternalIdKey = "$id";
    private const string InternalTypeKey = "$type";

    public Dictionary<string, KVModel> ChildModels { get; } = new(StringComparer.Ordinal);

    internal bool IsCollection { get; private set; }

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

    internal KVModel(KVOverlay overlay, string dataPath, bool isCollection = false)
    {
        Overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        DataPath = KVPath.Normalize(dataPath);
        IsCollection = isCollection;
    }

    public TValue Get<TValue>(string segment)
    {
        if (ChildModels.ContainsKey(segment)) throw new InvalidOperationException("Child store access");

        if (!Overlay.TryGet(ResolveDataPath(segment), out var value) || value?.Value is null)
        {
            return default!;
        }

        return value.Value is TValue typed
            ? typed
            : throw new InvalidCastException($"Stored value '{ResolveDataPath(segment)}' is '{value.Value.GetType().FullName}', not '{typeof(TValue).FullName}'.");
    }

    internal bool TryGetValue(string segment, out KVValue? value)
    {
        if (ChildModels.ContainsKey(segment)) throw new InvalidOperationException("Child store access");
        return Overlay.TryGet(ResolveDataPath(segment), out value);
    }

    public void Set<TValue>(string segment, TValue value)
    {
        if (ChildModels.ContainsKey(segment)) throw new InvalidOperationException("Child store access");
        Overlay.Set(ResolveDataPath(segment), new KVValue<TValue>(value));
    }

    internal void SetValue(string segment, KVValue value)
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

        return child;
    }

    public KVModel EnsureCollectionModel(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

        if (!ChildModels.TryGetValue(key, out var child))
        {
            child = new KVModel(Overlay, ResolveDataPath(key), isCollection: true);
            ChildModels[key] = child;
        }

        return child;
    }

    public KVModel EnsureItemModel(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

        if (!ChildModels.TryGetValue(key, out var child))
        {
            child = new KVModel(Overlay, ResolveDataPath(key));
            ChildModels[key] = child;
        }

        // Un-remove the path so re-adding a previously deleted item works correctly.
        Overlay.RestorePath(ResolveDataPath(key));
        return child;
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
        }
    }

    public void UnmarkChildRemoved(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);
        Overlay.RestorePath(ResolveDataPath(key));
    }

    public bool IsChildRemoved(string key)
    {
        ArgumentNullException.ThrowIfNullOrWhiteSpace(key);
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

    internal IReadOnlyList<string> DirectChildKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Overlay.Keys)
        {
            var relative = KVPath.RelativeTo(path, DataPath);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var firstSlash = relative.IndexOf('/');
            keys.Add(firstSlash < 0 ? relative : relative[..firstSlash]);
        }

        return new List<string>(keys);
    }

    internal void PruneDraftChildren(string discardedPath)
    {
        foreach (var child in new List<KeyValuePair<string, KVModel>>(ChildModels))
        {
            child.Value.PruneDraftChildren(discardedPath);
            if (IsCollection
                && KVPath.IsSameOrDescendant(child.Value.DataPath, discardedPath)
                && !child.Value.IsSnapshotBacked())
            {
                ChildModels.Remove(child.Key);
            }
        }
    }

    internal KVChangeDeltaGroup ComputeNodeDeltas(string nodePath, bool isCollectionItem)
    {
        var deltas = new List<KVChangeDelta>();

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
            if (IsInternalMetadataKey(key)) continue;

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
            if (IsInternalMetadataKey(removed)) continue;
            if (Overlay.IsSnapshotBacked(ResolveDataPath(removed)))
            {
                deltas.Add(new KVChangeDelta(KVPath.Combine(nodePath, removed), KVChangeDeltaType.Removed));
            }
        }

        var children = new List<KVChangeDeltaGroup>();

        foreach (var (childKey, childModel) in ChildModels)
        {
            var childPath = KVPath.Combine(nodePath, childKey);

            if (Overlay.IsRemoved(childModel.DataPath))
            {
                if (childModel.IsSnapshotBacked())
                {
                    deltas.Add(new KVChangeDelta(childPath, KVChangeDeltaType.Removed));
                }
                continue;
            }

            var childDeltas = childModel.ComputeNodeDeltas(childPath, IsCollection);
            if (childDeltas.Deltas.Count == 0 && childDeltas.Children.Count == 0) continue;
            children.Add(childDeltas);
        }

        return new KVChangeDeltaGroup(deltas, children);
    }

    private string ResolveDataPath(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return KVPath.Combine(DataPath, KVPath.Normalize(segment));
    }

    private bool IsSnapshotBacked() => Overlay.IsSnapshotBacked(DataPath);

    private bool HasDraftState()
    {
        if (Overlay.HasDraftState(DataPath)) return true;
        foreach (var child in ChildModels.Values)
        {
            if (child.HasDraftState()) return true;
        }
        return false;
    }

    private static bool TryCreateNestedNodeTypeDelta(string nodePath, KVOverlay overlay, out KVChangeDelta delta)
    {
        delta = default!;
        var typePath = KVPath.Combine(nodePath, InternalTypeKey);
        if (overlay.HasRemovedPath(typePath))
        {
            if (!overlay.TryGetSnapshotValue(typePath, out _)) return false;
            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Removed);
            return true;
        }

        if (!overlay.TryGetDraftValue(typePath, out var overlayValue)) return false;

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
