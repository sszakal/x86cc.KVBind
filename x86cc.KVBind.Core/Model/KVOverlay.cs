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
    }

    public Guid AggregateId => Snapshot.AggregateId;

    // The overlay's base (V1) is whatever snapshot it currently sits on — these track it directly.
    public Guid BaseSnapshotVersion => Snapshot.Version;

    public Guid? BaseCommitId => Snapshot.LastCommitId;

    public string User
    {
        get => _user;
        set => _user = !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Overlay user cannot be empty.", nameof(value));
    }

    public KVSnapshot Snapshot { get; private set; }

    // Single dictionary: regular KVValue = change, KVValue.Tombstone = deleted path (and its descendants).
    public Dictionary<string, KVValue> Changes { get; set; } = new(StringComparer.Ordinal);

    // ── Rebase state ──────────────────────────────────────────────────────────
    // While a rebase is in progress the overlay holds a frozen copy of the target snapshot (V2) and the
    // list of conflicts. The target is frozen at BeginRebase so the user resolves against a stable view
    // even if "latest" advances again; a later advance just triggers another rebase after this one finishes.

    public bool IsRebasing { get; private set; }

    public KVSnapshot? RebaseTarget { get; private set; }

    public List<KVConflict> Conflicts { get; private set; } = [];

    public bool HasUnresolvedConflicts => Conflicts.Any(conflict => !conflict.IsResolved);

    public bool HasChanges => Changes.Count > 0;

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

    // ── Rebase operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Starts a rebase of this overlay onto <paramref name="target"/> (V2). The target is frozen for the
    /// duration of the rebase. When <paramref name="autoMerge"/> is true and there are no conflicts, the
    /// rebase completes immediately (the base is fast-forwarded and the draft changes are kept). When there
    /// are conflicts, the overlay enters the rebasing state and the conflicts must be resolved before
    /// <see cref="FinishRebase"/>.
    /// </summary>
    public KVRebaseOutcome BeginRebase(KVSnapshot target, bool autoMerge = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (IsRebasing)
            throw new InvalidOperationException("A rebase is already in progress. Finish or cancel it first.");
        if (target.AggregateId != AggregateId)
            throw new InvalidOperationException("Cannot rebase onto a snapshot from a different aggregate.");

        if (target.Version == Snapshot.Version)
            return KVRebaseOutcome.AlreadyCurrent;

        var conflicts = KVMerge.ComputeConflicts(Snapshot, target, Changes);

        if (conflicts.Count == 0 && autoMerge)
        {
            // Fast-forward: no overlapping changes, so the target's changes simply show through once we
            // swap the base. The draft's own changes are untouched.
            Snapshot = target.Clone();
            return KVRebaseOutcome.Merged;
        }

        RebaseTarget = target.Clone();
        Conflicts = conflicts.ToList();
        IsRebasing = true;
        return conflicts.Count == 0 ? KVRebaseOutcome.Merged : KVRebaseOutcome.ConflictsPending;
    }

    public void ResolveConflict(string path, KVConflictResolution resolution, KVValue? customValue = null)
    {
        if (!IsRebasing)
            throw new InvalidOperationException("No rebase is in progress.");

        var conflict = Conflicts.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No conflict at path '{path}'.");

        conflict.Resolve(resolution, customValue);
    }

    /// <summary>
    /// Applies every conflict resolution to the draft, swaps the base to the frozen target and clears the
    /// rebase state. All conflicts must be resolved first.
    /// </summary>
    public void FinishRebase()
    {
        if (!IsRebasing)
            throw new InvalidOperationException("No rebase is in progress.");
        if (RebaseTarget is null)
            throw new InvalidOperationException("Rebase target is missing.");
        if (HasUnresolvedConflicts)
            throw new InvalidOperationException("Cannot finish a rebase while conflicts are unresolved.");

        foreach (var conflict in Conflicts)
            ApplyResolution(conflict);

        Snapshot = RebaseTarget;
        RebaseTarget = null;
        Conflicts = [];
        IsRebasing = false;
    }

    /// <summary>Aborts the rebase but keeps the draft changes — the overlay stays on its original (now stale) base.</summary>
    public void CancelRebase()
    {
        if (!IsRebasing)
            throw new InvalidOperationException("No rebase is in progress.");

        RebaseTarget = null;
        Conflicts = [];
        IsRebasing = false;
    }

    /// <summary>
    /// Discards all draft changes and resyncs the overlay onto <paramref name="target"/>. This is the
    /// "drop my changes" escape hatch from a rebase, and also the engine behind a general "start over".
    /// </summary>
    public void Reset(KVSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.AggregateId != AggregateId)
            throw new InvalidOperationException("Cannot reset onto a snapshot from a different aggregate.");

        Changes.Clear();
        Snapshot = target.Clone();
        RebaseTarget = null;
        Conflicts = [];
        IsRebasing = false;
    }

    /// <summary>Restores persisted rebase state when an in-progress rebase is reloaded from storage.</summary>
    public void RestoreRebaseState(KVSnapshot target, IEnumerable<KVConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(conflicts);
        RebaseTarget = target;
        Conflicts = conflicts.ToList();
        IsRebasing = true;
    }

    private void ApplyResolution(KVConflict conflict)
    {
        switch (conflict.Resolution)
        {
            case KVConflictResolution.Ours:
                // Keep the draft as-is. For a delete/edit conflict that means the tombstone stays.
                if (conflict.Kind == KVConflictKind.Structural)
                {
                    // Replace the whole subtree with ours: the draft's own values stay, but any target leaf
                    // under this path that the draft does NOT set is tombstoned, so the other type's fields
                    // (or dropped collection items) don't bleed through once the base swaps to the target.
                    foreach (var key in RebaseTarget!.Data.Keys)
                    {
                        if (!KVPath.IsSameOrDescendant(key, conflict.Path) || Changes.ContainsKey(key))
                            continue;
                        Changes[key] = KVValue.Tombstone;
                    }
                }
                break;

            case KVConflictResolution.Theirs:
                // Drop the draft's change(s) under this path so the target value shows through after the swap.
                foreach (var key in Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, conflict.Path)).ToList())
                    Changes.Remove(key);
                break;

            case KVConflictResolution.Custom:
                Changes[conflict.Path] = conflict.CustomValue
                    ?? throw new InvalidOperationException($"Custom resolution at '{conflict.Path}' has no value.");
                break;

            default:
                throw new InvalidOperationException($"Conflict at '{conflict.Path}' is unresolved.");
        }
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
