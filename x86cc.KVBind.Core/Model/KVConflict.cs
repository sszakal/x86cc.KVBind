using System;

namespace x86cc.KVBind.Core.Model;

/// <summary>The state of a started rebase — describes the review list, not whether it was finalized.</summary>
public enum KVRebaseOutcome
{
    /// <summary>The draft base is already at the latest commit — nothing to do, no rebase started.</summary>
    AlreadyCurrent = 0,

    /// <summary>
    /// Every entry can be merged automatically: there are no blocking conflicts, only incoming changes
    /// (each defaulting to accept). The rebase can be finalized as-is with <see cref="KVOverlay.FinishRebase"/>.
    /// Whether to surface the incoming changes for review first is up to the caller (UI vs. automated handler).
    /// </summary>
    CanAutomerge = 1,

    /// <summary>
    /// At least one real conflict needs a manual decision. The overlay stays in the rebasing state and
    /// <see cref="KVOverlay.FinishRebase"/> will throw until every conflict is resolved.
    /// </summary>
    HasUnresolvedConflicts = 2,
}

/// <summary>How a review entry may be resolved during a rebase.</summary>
public enum KVConflictResolution
{
    /// <summary>No decision yet. A real conflict cannot be finished while unresolved; incoming changes default to <see cref="Theirs"/>.</summary>
    Unresolved = 0,

    /// <summary>Keep the overlay's (draft) value. For an incoming change this means "reject" — pin the pre-rebase value.</summary>
    Ours = 1,

    /// <summary>Take the target snapshot's (main) value. For an incoming change this means "accept".</summary>
    Theirs = 2,

    /// <summary>Use an explicitly supplied value (plain value conflicts only).</summary>
    Custom = 3,
}

/// <summary>The shape of a review entry.</summary>
public enum KVConflictKind
{
    /// <summary>Conflict: both sides changed the same leaf to different values.</summary>
    Value = 0,

    /// <summary>Conflict: the overlay deleted a path (or subtree) that the target also modified (or vice versa).</summary>
    DeleteEdit = 1,

    /// <summary>
    /// Conflict: a whole-subtree clash that cannot be merged leaf-by-leaf — resolved entirely as Ours or Theirs.
    /// Produced when both sides changed a polymorphic node's discriminator (<c>$type</c>) to different types.
    /// Custom resolution is rejected.
    /// </summary>
    Structural = 2,

    /// <summary>
    /// Incoming (non-conflicting): the target changed a path or subtree the overlay did not touch. Defaults to
    /// <see cref="KVConflictResolution.Theirs"/> (accept). Rejecting it (<see cref="KVConflictResolution.Ours"/>)
    /// pins the pre-rebase (base) value as a counter-edit on the draft.
    /// </summary>
    Incoming = 3,

    /// <summary>
    /// Incoming (non-conflicting) collection membership change: the target added or removed a collection item
    /// the overlay did not touch. Accepting keeps the merged membership; rejecting reverses just this item.
    /// </summary>
    IncomingItem = 4,
}

/// <summary>
/// A single entry in a rebase review — either a real conflict that requires a decision, or a non-conflicting
/// incoming change that the user may accept (default) or reject. Values are nullable: <c>null</c> means the
/// path was absent on that side.
/// </summary>
public sealed class KVConflict
{
    public string Path { get; set; } = string.Empty;

    public KVConflictKind Kind { get; set; }

    /// <summary>Value at the common base (V1) — what the draft started from. <c>null</c> if absent.</summary>
    public KVValue? BaseValue { get; set; }

    /// <summary>Value in the rebase target (V2 / main). <c>null</c> if absent.</summary>
    public KVValue? MainValue { get; set; }

    /// <summary>The overlay's (draft) value. <c>null</c> if the overlay deleted the path or never touched it.</summary>
    public KVValue? OursValue { get; set; }

    public KVConflictResolution Resolution { get; set; } = KVConflictResolution.Unresolved;

    /// <summary>The explicit value chosen when <see cref="Resolution"/> is <see cref="KVConflictResolution.Custom"/>.</summary>
    public KVValue? CustomValue { get; set; }

    /// <summary>True for real conflicts that block <see cref="KVOverlay.FinishRebase"/> until resolved.</summary>
    public bool RequiresResolution => Kind is KVConflictKind.Value or KVConflictKind.DeleteEdit or KVConflictKind.Structural;

    /// <summary>True for non-conflicting incoming changes (default-accepted, individually rejectable).</summary>
    public bool IsIncoming => Kind is KVConflictKind.Incoming or KVConflictKind.IncomingItem;

    public bool IsResolved => Resolution != KVConflictResolution.Unresolved;

    public void Resolve(KVConflictResolution resolution, KVValue? customValue = null)
    {
        if (resolution == KVConflictResolution.Unresolved)
            throw new ArgumentException("Cannot resolve an entry back to Unresolved.", nameof(resolution));

        if (resolution == KVConflictResolution.Custom)
        {
            if (Kind == KVConflictKind.DeleteEdit)
                throw new InvalidOperationException(
                    $"Custom resolution is not valid for a delete/edit conflict at '{Path}'. Choose Ours (keep deletion) or Theirs (restore upstream).");
            if (Kind == KVConflictKind.Structural)
                throw new InvalidOperationException(
                    $"Custom resolution is not valid for a structural conflict at '{Path}'. Choose Ours or Theirs for the whole node.");
            if (IsIncoming)
                throw new InvalidOperationException(
                    $"Custom resolution is not valid for an incoming change at '{Path}'. Choose Theirs (accept) or Ours (reject).");
            CustomValue = customValue;
        }
        else
        {
            CustomValue = null;
        }

        Resolution = resolution;
    }
}
