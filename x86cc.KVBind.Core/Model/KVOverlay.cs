using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public sealed class KVOverlay
{
    private string _user = null!;

    public KVOverlay(KVSnapshot snapshot, string user)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        User = user;
        BaseSnapshotVersion = snapshot.Version;
        BaseCommitId = snapshot.LastCommitId;
    }

    public Guid AggregateId => Snapshot.AggregateId;

    public Guid BaseSnapshotVersion { get; }

    public Guid? BaseCommitId { get; }

    public string User
    {
        get => _user;
        set => _user = !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Overlay user cannot be empty.", nameof(value));
    }

    public KVSnapshot Snapshot { get; }

    public Dictionary<string, KVValue> AddedOrChanged { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> Removed { get; set; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            var keys = new HashSet<string>(Snapshot.Keys, StringComparer.Ordinal);
            keys.UnionWith(AddedOrChanged.Keys);
            keys.RemoveWhere(IsRemoved);
            return keys;
        }
    }

    public static KVOverlay Create(KVSnapshot snapshot, string user) => new(snapshot, user);

    public bool TryGet(string path, out KVValue? value)
    {
        if (IsRemoved(path))
        {
            value = default;
            return false;
        }

        if (AddedOrChanged.TryGetValue(path, out value))
        {
            return true;
        }

        return Snapshot.TryGet(path, out value);
    }

    public bool TryGetSnapshotValue(string path, out KVValue? value)
    {
        return Snapshot.TryGet(path, out value);
    }

    public bool TryGetDraftValue(string path, out KVValue? value)
    {
        return AddedOrChanged.TryGetValue(path, out value);
    }

    public bool HasRemovedPath(string path)
    {
        return Removed.Contains(path);
    }

    public void RestorePath(string path)
    {
        Removed.Remove(path);
    }

    public IEnumerable<KeyValuePair<string, object?>> DirectDraftValues(string parentPath, Func<string, bool>? excludeSegment = null)
    {
        foreach (var pair in AddedOrChanged)
        {
            if (KVPath.TryGetDirectSegment(pair.Key, parentPath, excludeSegment, out var segment))
            {
                yield return new KeyValuePair<string, object?>(segment, pair.Value.Value);
            }
        }
    }

    public IEnumerable<string> DirectKeys(string parentPath, Func<string, bool>? excludeSegment = null)
    {
        foreach (var key in Keys)
        {
            if (KVPath.TryGetDirectSegment(key, parentPath, excludeSegment, out var segment))
            {
                yield return segment;
            }
        }
    }

    public IEnumerable<string> DirectRemovedValues(string parentPath, Func<string, bool>? excludeSegment = null)
    {
        foreach (var removed in Removed)
        {
            if (KVPath.TryGetDirectSegment(removed, parentPath, excludeSegment, out var segment))
            {
                yield return segment;
            }
        }
    }

    public void Set(string path, KVValue value)
    {
        RemoveDescendantRemovalMarkers(path);
        Removed.Remove(path);
        AddedOrChanged[path] = value;
    }

    public bool Remove(string path)
    {
        var hadValue = AddedOrChanged.Remove(path) || HasAddedOrChangedDescendant(path) || SnapshotContains(path);
        RemoveAddedOrChangedDescendants(path);
        Removed.Add(path);
        return hadValue;
    }

    public void Clear()
    {
        AddedOrChanged.Clear();
        Removed.Clear();
    }

    public void Discard(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = KVPath.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            Clear();
            return;
        }

        AddedOrChanged.Remove(normalizedPath);
        RemoveAddedOrChangedDescendants(normalizedPath);
        Removed.RemoveWhere(removed => KVPath.IsSameOrDescendant(removed, normalizedPath));
    }

    public bool IsSnapshotBacked(string path)
    {
        return Snapshot.ContainsPathOrDescendant(path);
    }

    public bool HasDraftState(string path)
    {
        foreach (var key in AddedOrChanged.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path))
            {
                return true;
            }
        }

        foreach (var key in Removed)
        {
            if (KVPath.IsSameOrDescendant(key, path))
            {
                return true;
            }
        }

        return false;
    }

    public KVCommit ToCommit(DateTimeOffset timestamp)
    {
        return new KVCommit
        {
            AggregateId = AggregateId,
            CommitId = Guid.NewGuid(),
            PreviousCommitId = BaseCommitId,
            User = User,
            Timestamp = timestamp,
            AddedOrChanged = new Dictionary<string, KVValue>(AddedOrChanged, StringComparer.Ordinal),
            Removed = new HashSet<string>(Removed, StringComparer.Ordinal)
        };
    }

    internal bool IsRemoved(string path)
    {
        foreach (var removed in Removed)
        {
            if (KVPath.IsSameOrDescendant(path, removed))
            {
                return true;
            }
        }

        return false;
    }

    private bool SnapshotContains(string path)
    {
        if (Snapshot.Data.ContainsKey(path))
        {
            return true;
        }

        foreach (var key in Snapshot.Data.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAddedOrChangedDescendant(string path)
    {
        foreach (var key in AddedOrChanged.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path) && !string.Equals(key, path, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void RemoveAddedOrChangedDescendants(string path)
    {
        var keysToRemove = new List<string>();
        foreach (var key in AddedOrChanged.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path) && !string.Equals(key, path, StringComparison.Ordinal))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            AddedOrChanged.Remove(key);
        }
    }

    private void RemoveDescendantRemovalMarkers(string path)
    {
        Removed.RemoveWhere(removed => KVPath.IsSameOrDescendant(removed, path) && !string.Equals(removed, path, StringComparison.Ordinal));
    }
}
