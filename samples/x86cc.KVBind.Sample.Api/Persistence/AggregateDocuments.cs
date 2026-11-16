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

/// <summary>
/// The committed state of an aggregate as it was at one commit, kept only while a draft is still based on
/// it (the aggregate's own document has moved on). Its id is that commit's id.
/// </summary>
public sealed class AggregateBaseSnapshotDocument
{
    public Guid Id { get; set; }

    public string Model { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public KVSnapshot Snapshot { get; set; } = new();
}

/// <summary>
/// A user's open draft of one aggregate: the changes, the id of the commit they are based on, and any rebase
/// in progress. The base snapshot itself is not stored here, so saving a draft writes only what the user
/// changed: it is the aggregate's current snapshot while the draft is up to date, and an
/// <see cref="AggregateBaseSnapshotDocument"/> once the aggregate has moved on. The service attaches it
/// (<see cref="Base"/>) when it loads the draft.
/// </summary>
public sealed class AggregateDraftDocument
{
    public Guid Id { get; set; }

    public string Model { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public string User { get; set; } = string.Empty;

    /// <summary>The commit the draft's changes are based on.</summary>
    public Guid? BaseCommitId { get; set; }

    /// <summary>Only in drafts stored by earlier versions, which embedded their base snapshot; cleared on the next save.</summary>
    public KVSnapshot? Snapshot { get; set; }

    /// <summary>The base snapshot, attached after loading; not stored.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public KVSnapshot? Base { get; set; }

    public KVDictionary Changes { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsRebasing { get; set; }

    // Frozen target snapshot (V2) the rebase resolves against. Null unless rebasing.
    public KVSnapshot? RebaseTarget { get; set; }

    public List<KVConflict> Conflicts { get; set; } = [];

    /// <summary>Domain events raised by patches to this draft; published with the commit, dropped on reset.</summary>
    public List<AggregateEventRecord> PendingEvents { get; set; } = [];

    /// <summary>
    /// The draft's changes that did not survive moving the draft onto a migrated snapshot: replaced by the
    /// committed value, or removed by a migration. Shown to the user until the draft is committed or reset.
    /// </summary>
    public List<DraftMigrationNotice> MigrationNotices { get; set; } = [];

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
        var baseSnapshot = this.Base ?? throw new InvalidOperationException($"The base snapshot of draft '{this.Id}' was not attached.");
        var overlay = KVOverlay.Create(baseSnapshot.Clone(), this.User);

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
        this.Base = overlay.Snapshot;
        this.BaseCommitId = overlay.Snapshot.LastCommitId;
        this.Snapshot = null;
        this.Changes = new KVDictionary(overlay.Changes);
        this.IsRebasing = overlay.IsRebasing;
        this.RebaseTarget = overlay.RebaseTarget?.Clone();
        this.Conflicts = [.. overlay.Conflicts];
        this.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// One draft change lost when the draft was moved onto a migrated snapshot. <see cref="Kind"/> is
/// <c>Replaced</c> (it conflicted with a committed change, which won), <c>Reordered</c> (the draft's order of
/// the collection at <see cref="Path"/> gave way to the committed one; the values are the item id lists) or
/// <c>Removed</c> (a migration deleted the field). <see cref="Path"/> is in the layout of <see cref="SchemaVersion"/>.
/// </summary>
public sealed class DraftMigrationNotice
{
    public string Path { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public object? DraftValue { get; set; }

    public object? CommittedValue { get; set; }

    public int SchemaVersion { get; set; }
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
