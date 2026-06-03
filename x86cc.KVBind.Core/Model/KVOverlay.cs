using System;
using System.Collections.Generic;
using System.Linq;

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

    // Single dictionary: regular KVValue = change, KVValue.Tombstone = deleted path (and its descendants).
    public Dictionary<string, KVValue> Changes { get; set; } = new(StringComparer.Ordinal);

    public static KVOverlay Create(KVSnapshot snapshot, string user) => new(snapshot, user);

    public bool TryGet(string path, out KVValue? value)
    {
        if (IsRemoved(path)) { value = default; return false; }
        if (Changes.TryGetValue(path, out value)) return true; // not tombstone since IsRemoved would have caught it
        value = null;
        return Snapshot.TryGet(path, out value);
    }

    public bool TryGetSnapshotValue(string path, out KVValue? value) => Snapshot.TryGet(path, out value);

    public bool TryGetDraftValue(string path, out KVValue? value)
    {
        if (Changes.TryGetValue(path, out value) && value != KVValue.Tombstone) return true;
        value = null;
        return false;
    }

    // True if this exact path has a tombstone (not an ancestor).
    public bool HasRemovedPath(string path) =>
        Changes.TryGetValue(path, out var v) && v == KVValue.Tombstone;

    // True if path or any ancestor has a tombstone.
    public bool IsRemoved(string path)
    {
        if (HasRemovedPath(path)) return true;
        var p = path;
        var slash = p.LastIndexOf('/');
        while (slash >= 0)
        {
            p = p[..slash];
            if (HasRemovedPath(p)) return true;
            slash = p.LastIndexOf('/');
        }
        return false;
    }

    // Remove a direct tombstone at this path (un-delete without touching descendants).
    public void RestorePath(string path)
    {
        if (Changes.TryGetValue(path, out var v) && v == KVValue.Tombstone)
            Changes.Remove(path);
    }

    public void Set(string path, KVValue value)
    {
        Changes[path] = value;
    }

    public bool Remove(string path)
    {
        var hadValue = Changes.ContainsKey(path)
                    || Changes.Keys.Any(k => KVPath.IsSameOrDescendant(k, path) && !string.Equals(k, path, StringComparison.Ordinal))
                    || Snapshot.ContainsPathOrDescendant(path);

        // Clear all descendant entries
        foreach (var key in Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, path)).ToList())
            Changes.Remove(key);

        Changes[path] = KVValue.Tombstone;
        return hadValue;
    }

    public void Clear() => Changes.Clear();

    public void Discard(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = KVPath.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized)) { Clear(); return; }

        foreach (var key in Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, normalized)).ToList())
            Changes.Remove(key);
    }

    public bool IsSnapshotBacked(string path) => Snapshot.ContainsPathOrDescendant(path);

    public bool HasDraftState(string path)
    {
        foreach (var key in Changes.Keys)
            if (KVPath.IsSameOrDescendant(key, path)) return true;
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
            Changes = new Dictionary<string, KVValue>(Changes, StringComparer.Ordinal)
        };
    }
}
