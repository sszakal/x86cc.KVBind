using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

public sealed class KVSnapshot
{
    public Guid AggregateId { get; set; } = Guid.NewGuid();

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public Guid? LastCommitId { get; set; }

    public DateTimeOffset? LastCommitTimestamp { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;

    public string ModifiedBy { get; set; } = string.Empty;

    public Guid Version { get; set; } = Guid.NewGuid();

    public Dictionary<string, KVValue> Data { get; set; } = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Keys => Data.Keys;

    public KVSnapshot Clone()
    {
        return new KVSnapshot
        {
            AggregateId = AggregateId,
            Timestamp = Timestamp,
            LastCommitId = LastCommitId,
            LastCommitTimestamp = LastCommitTimestamp,
            Created = Created,
            CreatedBy = CreatedBy,
            Modified = Modified,
            ModifiedBy = ModifiedBy,
            Version = Version,
            Data = new Dictionary<string, KVValue>(Data, StringComparer.Ordinal)
        };
    }

    public bool TryGet(string path, out KVValue? value)
    {
        return Data.TryGetValue(path, out value);
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

        if (commit.AggregateId != AggregateId)
        {
            throw new InvalidOperationException("Commit aggregate id does not match snapshot aggregate id.");
        }

        if (commit.PreviousCommitId != LastCommitId)
        {
            throw new InvalidOperationException("Commit does not continue the snapshot commit chain.");
        }

        foreach (var removed in commit.Removed)
        {
            RemovePathOrPrefix(Data, removed);
        }

        foreach (var pair in commit.AddedOrChanged)
        {
            Data[pair.Key] = pair.Value;
        }

        LastCommitId = commit.CommitId;
        LastCommitTimestamp = commit.Timestamp;
        Modified = commit.Timestamp;
        ModifiedBy = commit.User;
        Timestamp = DateTimeOffset.UtcNow;
        Version = Guid.NewGuid();
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
