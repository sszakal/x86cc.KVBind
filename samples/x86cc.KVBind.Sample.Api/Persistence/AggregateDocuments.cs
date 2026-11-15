using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Sample.Api.Persistence;

// The generic API stores every model in the same three document types, tagged with the model name. KVBind
// itself only produces snapshots, overlays and commits; identity, ownership and concurrency are ours.

/// <summary>The committed state of one aggregate.</summary>
public sealed class AggregateSnapshotDocument
{
    public Guid Id { get; set; }

    public string Model { get; set; } = string.Empty;

    /// <summary>Set for a child aggregate: the aggregate its inherited members are read from.</summary>
    public Guid? ParentId { get; set; }

    public KVSnapshot Snapshot { get; set; } = new();
}

/// <summary>A user's open draft (overlay) of one aggregate, including any rebase in progress.</summary>
public sealed class AggregateDraftDocument
{
    public Guid Id { get; set; }

    public string Model { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public string User { get; set; } = string.Empty;

    public KVSnapshot Snapshot { get; set; } = new();

    public KVDictionary Changes { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsRebasing { get; set; }

    // Frozen target snapshot (V2) the rebase resolves against. Null unless rebasing.
    public KVSnapshot? RebaseTarget { get; set; }

    public List<KVConflict> Conflicts { get; set; } = [];

    /// <summary>Domain events raised by patches to this draft; published with the commit, dropped on reset.</summary>
    public List<AggregateEventRecord> PendingEvents { get; set; } = [];

    public Guid? BaseCommitId => this.Snapshot.LastCommitId;

    public static AggregateDraftDocument Create(string model, Guid aggregateId, string user, KVOverlay overlay)
    {
        var document = new AggregateDraftDocument
        {
            Id = DraftId(aggregateId, user),
            Model = model,
            AggregateId = aggregateId,
            User = user,
        };
        document.UpdateFrom(overlay);
        return document;
    }

    // A user has at most one open draft per aggregate, so (AggregateId, User) is the identity. Marten keys a
    // document by a single member, so a deterministic Guid is derived from the pair. MD5 only spreads the
    // pair over a Guid; nothing relies on it being hard to reverse, and changing it would orphan stored drafts.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S4790:Use a stronger hashing algorithm", Justification = "Not a security use: a stable id derived from (aggregate, user).")]
    public static Guid DraftId(Guid aggregateId, string user)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{aggregateId:D}:{user}"));
        return new Guid(bytes);
    }

    public KVOverlay ToOverlay()
    {
        var overlay = KVOverlay.Create(this.Snapshot.Clone(), this.User);

        // Defensive copy so overlay edits don't mutate this (Marten-tracked) document before UpdateFrom.
        overlay.Changes = new KVDictionary(this.Changes);
        if (this.IsRebasing && this.RebaseTarget is not null)
        {
            overlay.RestoreRebaseState(this.RebaseTarget.Clone(), this.Conflicts);
        }

        return overlay;
    }

    public void UpdateFrom(KVOverlay overlay)
    {
        this.Snapshot = overlay.Snapshot.Clone();
        this.Changes = new KVDictionary(overlay.Changes);
        this.IsRebasing = overlay.IsRebasing;
        this.RebaseTarget = overlay.RebaseTarget?.Clone();
        this.Conflicts = [.. overlay.Conflicts];
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>One commit of an aggregate, with its projected change rows and the domain events it published.</summary>
public sealed class AggregateChangeSetDocument
{
    public Guid Id { get; set; }

    public string Model { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public KVCommit Commit { get; set; } = new();

    public List<AggregateChangeRow> Changes { get; set; } = [];

    public List<AggregateEventRecord> Events { get; set; } = [];
}

/// <summary>A change as the history shows it: display path, kind, and labelled old/new values.</summary>
public sealed record AggregateChangeRow(string Path, string ChangeType, object? OldValue = null, object? NewValue = null);

/// <summary>A domain event as stored: its type name and a JSON payload.</summary>
public sealed record AggregateEventRecord(string Type, string Payload, DateTimeOffset RaisedAt);
