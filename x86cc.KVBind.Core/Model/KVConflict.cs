using System;

namespace x86cc.KVBind.Core.Model;

/// <summary>The result of starting a rebase.</summary>
public enum KVRebaseOutcome
{
    /// <summary>The overlay was already on the target snapshot — nothing to do.</summary>
    AlreadyCurrent = 0,

    /// <summary>The rebase merged cleanly with no conflicts; the base was fast-forwarded.</summary>
    Merged = 1,

    /// <summary>Conflicts were found; the overlay is now in the rebasing state awaiting resolution.</summary>
    ConflictsPending = 2,
}

/// <summary>How a conflicting path may be resolved during a rebase.</summary>
public enum KVConflictResolution
{
    /// <summary>No decision yet — the rebase cannot be finished while any conflict is unresolved.</summary>
    Unresolved = 0,

    /// <summary>Keep the overlay's (draft) value — "keep mine".</summary>
    Ours = 1,

    /// <summary>Take the target snapshot's (main) value — "take theirs".</summary>
    Theirs = 2,

    /// <summary>Use an explicitly supplied value (value conflicts only).</summary>
    Custom = 3,
}

/// <summary>The shape of a conflict.</summary>
public enum KVConflictKind
{
    /// <summary>Both sides changed the same leaf to different values.</summary>
    Value = 0,

    /// <summary>The overlay deleted a path (or subtree) that the target also modified.</summary>
    DeleteEdit = 1,

    /// <summary>
    /// A whole-subtree conflict that cannot be merged leaf-by-leaf — resolved entirely as Ours or Theirs.
    /// Produced when both sides changed a polymorphic node's discriminator (<c>$type</c>) to different
    /// types, or both changed a collection's membership array (<c>$items</c>). Custom resolution is rejected.
    /// </summary>
    Structural = 2,
}

/// <summary>
/// A single three-way-merge conflict produced by <see cref="KVMerge"/> during a rebase.
/// All values are nullable: <c>null</c> means the path was absent on that side
/// (for <see cref="KVConflictKind.DeleteEdit"/>, <see cref="OursValue"/> is always <c>null</c> — the overlay deleted it).
/// </summary>
public sealed class KVConflict
{
    public string Path { get; set; } = string.Empty;

    public KVConflictKind Kind { get; set; }

    /// <summary>Value at the common base (V1) — what both sides started from. <c>null</c> if absent.</summary>
    public KVValue? BaseValue { get; set; }

    /// <summary>Value in the rebase target (V2 / main). <c>null</c> if absent.</summary>
    public KVValue? MainValue { get; set; }

    /// <summary>The overlay's (draft) value. <c>null</c> if the overlay deleted the path.</summary>
    public KVValue? OursValue { get; set; }

    public KVConflictResolution Resolution { get; set; } = KVConflictResolution.Unresolved;

    /// <summary>The explicit value chosen when <see cref="Resolution"/> is <see cref="KVConflictResolution.Custom"/>.</summary>
    public KVValue? CustomValue { get; set; }

    public bool IsResolved => Resolution != KVConflictResolution.Unresolved;

    public void Resolve(KVConflictResolution resolution, KVValue? customValue = null)
    {
        if (resolution == KVConflictResolution.Unresolved)
            throw new ArgumentException("Cannot resolve a conflict back to Unresolved.", nameof(resolution));

        if (resolution == KVConflictResolution.Custom)
        {
            if (Kind == KVConflictKind.DeleteEdit)
                throw new InvalidOperationException(
                    $"Custom resolution is not valid for a delete/edit conflict at '{Path}'. Choose Ours (keep deletion) or Theirs (restore upstream).");
            if (Kind == KVConflictKind.Structural)
                throw new InvalidOperationException(
                    $"Custom resolution is not valid for a structural conflict at '{Path}'. Choose Ours or Theirs for the whole node.");
            CustomValue = customValue;
        }
        else
        {
            CustomValue = null;
        }

        Resolution = resolution;
    }
}
