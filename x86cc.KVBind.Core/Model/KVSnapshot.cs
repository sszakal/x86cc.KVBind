using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public sealed class KVSnapshot
{
    // Identity (which stream this is) and the optimistic-concurrency token are consumer concerns — they
    // live on the consumer's wrapper, not here. The commit chain (LastCommitId) anchors the snapshot.
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public Guid? LastCommitId { get; set; }

    public DateTimeOffset? LastCommitTimestamp { get; set; }

    // Audit, maintained as a derived projection of the commit log (first/last commit's who/when).
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    public string ModifiedBy { get; set; } = string.Empty;

    // Layout/schema version the persisted data conforms to. Advanced only by migration commits (see
    // KVCommit.MigrationToVersion). Compared against the code's CurrentSchemaVersion to decide migration:
    // less = migrate forward, equal = current, greater = data is newer than this binary (must refuse).
    public int SchemaVersion { get; set; }

    public KVDictionary Data { get; set; } = new();

    public KVSnapshot Clone()
    {
        return new KVSnapshot
        {
            Timestamp = Timestamp,
            LastCommitId = LastCommitId,
            LastCommitTimestamp = LastCommitTimestamp,
            Created = Created,
            CreatedBy = CreatedBy,
            Modified = Modified,
            ModifiedBy = ModifiedBy,
            SchemaVersion = SchemaVersion,
            Data = new KVDictionary(Data)
        };
    }

    public bool TryGet(string path, out KVValue? value)
    {
        return Data.TryGetValue(path, out value);
    }

    // Span overload for the allocation-free read path — probes Data via its Ordinal alternate lookup.
    public bool TryGet(ReadOnlySpan<char> path, out KVValue? value)
    {
        if (Data.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(path, out var found))
        {
            value = found;
            return true;
        }

        value = null;
        return false;
    }

    public bool ContainsPathOrDescendant(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Data.Count > 0;
        }

        if (Data.ContainsKey(path))
        {
            return true;
        }

        foreach (var key in Data.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path))
            {
                return true;
            }
        }

        return false;
    }

    public void Apply(KVCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        if (commit.PreviousCommitId != LastCommitId)
        {
            throw new InvalidOperationException("Commit does not continue the snapshot commit chain.");
        }

        foreach (var (path, value) in commit.Changes)
        {
            if (value == KVValue.Tombstone)
                RemovePathOrPrefix(Data, path);
            else
                Data[path] = value;
        }

        // A migration commit advances the schema version — even with no data changes (a pure version bump).
        if (commit.MigrationToVersion is int migratedTo)
            SchemaVersion = migratedTo;

        LastCommitId = commit.CommitId;
        LastCommitTimestamp = commit.Timestamp;
        Modified = commit.Timestamp;
        ModifiedBy = commit.User;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public void Apply(IEnumerable<KVCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);

        foreach (var commit in commits)
        {
            Apply(commit);
        }
    }

    private static void RemovePathOrPrefix(Dictionary<string, KVValue> data, string path)
    {
        if (data.Remove(path))
        {
            return;
        }

        var keysToRemove = new List<string>();
        foreach (var key in data.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path))
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            data.Remove(key);
        }
    }
}
