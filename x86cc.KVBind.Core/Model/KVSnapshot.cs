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
            Timestamp = this.Timestamp,
            LastCommitId = this.LastCommitId,
            LastCommitTimestamp = this.LastCommitTimestamp,
            Created = this.Created,
            CreatedBy = this.CreatedBy,
            Modified = this.Modified,
            ModifiedBy = this.ModifiedBy,
            SchemaVersion = this.SchemaVersion,
            Data = new KVDictionary(this.Data)
        };
    }

    public bool TryGet(string path, out KVValue? value)
    {
        return this.Data.TryGetValue(path, out value);
    }

    // Span overload for the allocation-free read path — probes Data via its Ordinal alternate lookup.
    public bool TryGet(ReadOnlySpan<char> path, out KVValue? value)
    {
        if (this.Data.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(path, out var found))
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
            return this.Data.Count > 0;
        }

        if (this.Data.ContainsKey(path))
        {
            return true;
        }

        foreach (var key in this.Data.Keys)
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

        if (commit.PreviousCommitId != this.LastCommitId)
        {
            throw new InvalidOperationException("Commit does not continue the snapshot commit chain.");
        }

        foreach (var (path, value) in commit.Changes)
        {
            if (value == KVValue.Tombstone)
                RemovePathOrPrefix(this.Data, path);
            else
                this.Data[path] = value;
        }

        // A migration commit advances the schema version — even with no data changes (a pure version bump).
        if (commit.MigrationToVersion is int migratedTo)
            this.SchemaVersion = migratedTo;

        this.LastCommitId = commit.CommitId;
        this.LastCommitTimestamp = commit.Timestamp;
        this.Modified = commit.Timestamp;
        this.ModifiedBy = commit.User;
        this.Timestamp = DateTimeOffset.UtcNow;
    }

    public void Apply(IEnumerable<KVCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(commits);

        foreach (var commit in commits)
        {
            this.Apply(commit);
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
