using System;
using System.Collections.Generic;
using System.Linq;

namespace x86cc.KVBind.Core.Model;

public sealed class KVOverlay
{
    // A reserved root key persisting "this aggregate is a child of a parent" (value true). Its presence in
    // the effective state marks an aggregate as a child; the consumer holds the actual parent id and must
    // supply the parent snapshot at bind. Detach tombstones it. Not an inherited path, so reads/writes of it
    // are unaffected by the inheritance layer.
    internal const string ChildMarkKey = "$parent";

    // ── Inheritance (transient: injected at bind, never serialized or committed) ──────────────────────────
    // When a child aggregate is bound with its parent's snapshot, the inherited root members (and their whole
    // subtrees) read from the parent and reject writes. A parentless master has no inheritance — those same
    // members are then normal editable fields. The prefixes come from KVNodeDefinition.InheritedPrefixes.
    private KVSnapshot? inheritedSource;

    private string[] inheritedPrefixes = [];

    public KVOverlay(KVSnapshot snapshot, string user)
    {
        this.Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        this.User = user;
    }

    // The overlay's base (V1) is whatever snapshot it currently sits on — the commit chain anchors it.
    public Guid? BaseCommitId => this.Snapshot.LastCommitId;

    public string User
    {
        get;
        set => field = !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Overlay user cannot be empty.", nameof(value));
    }

= null!;

    public KVSnapshot Snapshot { get; private set; }

    // Single dictionary: regular KVValue = change, KVValue.Tombstone = deleted path (and its descendants).
    public KVDictionary Changes { get; set; } = new();

    public bool HasInheritance => this.inheritedSource is not null;

    // Effective presence of the child mark (Changes over Snapshot): true for a freshly-marked child (staged)
    // and a loaded child (committed); false once detached (tombstoned). Checked before inheritance is set.
    public bool IsMarkedChild => this.TryGet(ChildMarkKey, out _);

    // ── Rebase state ──────────────────────────────────────────────────────────
    // While a rebase is in progress the overlay holds a frozen copy of the target snapshot (V2) and the
    // list of conflicts. The target is frozen at BeginRebase so the user resolves against a stable view
    // even if "latest" advances again; a later advance just triggers another rebase after this one finishes.

    public bool IsRebasing { get; private set; }

    public KVSnapshot? RebaseTarget { get; private set; }

    public List<KVConflict> Conflicts { get; private set; } = [];

    // Only real conflicts block finishing. Incoming changes carry a default (accept) and never block.
    public bool HasUnresolvedConflicts => this.Conflicts.Any(c => c.RequiresResolution && !c.IsResolved);

    public bool HasChanges => this.Changes.Count > 0;

    public static KVOverlay Create(KVSnapshot snapshot, string user) => new(snapshot, user);

    // ── Rebase operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Folds a sequence of commits into a "theirs" overlay over <paramref name="baseSnapshot"/> by replaying
    /// each commit's changes through the overlay's own <see cref="Set"/> / <see cref="Remove"/> (so
    /// last-write-wins and prefix-tombstone semantics fall out for free), then normalizes against the base
    /// — dropping entries equal to the base value and tombstones for paths the base never had. The result
    /// is the upstream change-set as another overlay sharing the same base; a full undo collapses to empty.
    /// </summary>
    public static KVOverlay FromCommits(KVSnapshot baseSnapshot, IEnumerable<KVCommit> commits)
    {
        ArgumentNullException.ThrowIfNull(baseSnapshot);
        ArgumentNullException.ThrowIfNull(commits);

        var overlay = Create(baseSnapshot, "upstream");
        foreach (var commit in commits)
        {
            foreach (var (path, value) in commit.Changes)
            {
                if (value == KVValue.Tombstone) overlay.Remove(path);
                else overlay.Set(path, value);
            }
        }

        overlay.NormalizeAgainstBase();
        return overlay;
    }

    public void MarkAsChild() => this.Set(ChildMarkKey, KVValue.FromObject(true));

    public void SetInheritance(KVSnapshot parentSource, IReadOnlyList<string> inheritedPrefixes)
    {
        ArgumentNullException.ThrowIfNull(parentSource);
        ArgumentNullException.ThrowIfNull(inheritedPrefixes);
        this.inheritedSource = parentSource;
        this.inheritedPrefixes = inheritedPrefixes as string[] ?? inheritedPrefixes.ToArray();
    }

    /// <summary>
    /// Severs the parent link: copies every inherited value from the parent into this overlay's draft
    /// <see cref="Changes"/> (so it becomes the child's own, editable data) and clears the inheritance.
    /// The copy is a normal draft edit — commit it via <see cref="ToCommit"/> to persist it into the child
    /// snapshot. No-op when not inheriting. After committing, stop supplying the parent at future binds.
    /// </summary>
    public void Detach()
    {
        var wasMarkedChild = this.IsMarkedChild;

        if (this.inheritedSource is not null)
        {
            var source = this.inheritedSource;
            var prefixes = this.inheritedPrefixes;

            // Clear the link first so the copy writes are permitted (the guard no longer fires).
            this.inheritedSource = null;
            this.inheritedPrefixes = [];

            foreach (var (key, value) in source.Data)
            {
                foreach (var prefix in prefixes)
                {
                    if (KVPath.IsSameOrDescendant(key, prefix))
                    {
                        this.Changes[key] = value;
                        break;
                    }
                }
            }
        }

        // Remove the child mark in the same draft/commit so a later rebind no longer requires a parent.
        if (wasMarkedChild)
            this.Changes[ChildMarkKey] = KVValue.Tombstone;
    }

    public bool TryGet(string path, out KVValue? value)
    {
        if (this.inheritedSource is not null && this.IsInheritedPath(path)) return this.inheritedSource.TryGet(path, out value);
        if (this.IsRemoved(path))
        {
            value = default;
            return false;
        }

        if (this.Changes.TryGetValue(path, out value)) return true; // not tombstone since IsRemoved would have caught it
        value = null;
        return this.Snapshot.TryGet(path, out value);
    }

    // Span overload: lets callers probe an assembled path without first allocating it as a string
    // (the field-read hot path builds the path in a stack buffer). Uses the dictionaries' Ordinal
    // alternate lookup, so it is allocation-free.
    public bool TryGet(ReadOnlySpan<char> path, out KVValue? value)
    {
        if (this.inheritedSource is not null && this.IsInheritedPath(path)) return this.inheritedSource.TryGet(path, out value);
        if (this.IsRemoved(path))
        {
            value = default;
            return false;
        }

        if (this.Changes.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(path, out value)) return true;
        value = null;
        return this.Snapshot.TryGet(path, out value);
    }

    public bool TryGetSnapshotValue(string path, out KVValue? value) => this.Snapshot.TryGet(path, out value);

    public bool TryGetDraftValue(string path, out KVValue? value)
    {
        if (this.Changes.TryGetValue(path, out value) && value != KVValue.Tombstone) return true;
        value = null;
        return false;
    }

    // True if this exact path has a tombstone (not an ancestor).
    public bool HasRemovedPath(string path) =>
        this.Changes.TryGetValue(path, out var v) && v == KVValue.Tombstone;

    // True if path or any ancestor has a tombstone.
    public bool IsRemoved(string path) => this.IsRemoved(path.AsSpan());

    // Walk the path and its ancestors over a span, probing via the dictionary's span alternate lookup so
    // the common (no-tombstone) case allocates nothing — instead of slicing a new substring per level.
    public bool IsRemoved(ReadOnlySpan<char> path)
    {
        if (this.Changes.Count == 0) return false;

        var lookup = this.Changes.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(path, out var exact) && exact == KVValue.Tombstone) return true;

        var span = path;
        var slash = span.LastIndexOf('/');
        while (slash >= 0)
        {
            span = span[..slash];
            if (lookup.TryGetValue(span, out var v) && v == KVValue.Tombstone) return true;
            slash = span.LastIndexOf('/');
        }

        return false;
    }

    // Remove a direct tombstone at this path (un-delete without touching descendants).
    public void RestorePath(string path)
    {
        if (this.Changes.TryGetValue(path, out var v) && v == KVValue.Tombstone)
            this.Changes.Remove(path);
    }

    // Un-delete a tombstoned path as an empty subtree: the direct tombstone is lifted and every snapshot
    // key beneath it is tombstoned individually, so re-creating the path never resurrects old values.
    public void ReviveRemovedPath(string path)
    {
        if (!this.Changes.TryGetValue(path, out var v) || v != KVValue.Tombstone) return;

        this.Changes.Remove(path);
        foreach (var key in this.Snapshot.Data.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path))
                this.Changes[key] = KVValue.Tombstone;
        }
    }

    // Wipes a subtree back to "nothing stored", keeping `keepKey` (e.g. a $type discriminator): draft
    // entries under the path are dropped and every snapshot key under it is tombstoned individually. The
    // path itself stays live, so a fresh object can be written into it in place.
    public void ResetSubtree(string path, string? keepKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.GuardWritable(path);

        foreach (var key in this.Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, path) && !string.Equals(k, keepKey, StringComparison.Ordinal)).ToList())
            this.Changes.Remove(key);

        foreach (var key in this.Snapshot.Data.Keys)
        {
            if (KVPath.IsSameOrDescendant(key, path) && !string.Equals(key, keepKey, StringComparison.Ordinal))
                this.Changes[key] = KVValue.Tombstone;
        }
    }

    public void Set(string path, KVValue value)
    {
        this.GuardWritable(path);
        this.Changes[path] = value;
    }

    public bool Remove(string path)
    {
        this.GuardWritable(path);
        var hadValue = this.Changes.ContainsKey(path)
                    || this.Changes.Keys.Any(k => KVPath.IsSameOrDescendant(k, path) && !string.Equals(k, path, StringComparison.Ordinal))
                    || this.Snapshot.ContainsPathOrDescendant(path);

        // Clear all descendant entries
        foreach (var key in this.Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, path)).ToList())
            this.Changes.Remove(key);

        this.Changes[path] = KVValue.Tombstone;
        return hadValue;
    }

    public void Clear() => this.Changes.Clear();

    public void Discard(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = KVPath.Normalize(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            this.Clear();
            return;
        }

        foreach (var key in this.Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, normalized)).ToList())
            this.Changes.Remove(key);
    }

    public bool IsSnapshotBacked(string path) => this.Snapshot.ContainsPathOrDescendant(path);

    public bool HasDraftState(string path)
    {
        foreach (var key in this.Changes.Keys)
            if (KVPath.IsSameOrDescendant(key, path)) return true;
        return false;
    }

    /// <summary>
    /// Starts a rebase of this draft over the upstream <paramref name="missingCommits"/> (the commits made
    /// since the draft's base). The diff is driven entirely by those commits; <paramref name="target"/>
    /// (the latest snapshot) supplies the identity stamped onto the draft's base when the rebase finishes.
    ///
    /// Always produces a review list — non-conflicting incoming changes (default: accept) and real conflicts
    /// (no default) — and enters the rebasing state; the caller finalizes with <see cref="FinishRebase"/>.
    /// Returns <see cref="KVRebaseOutcome.CanAutomerge"/> when nothing blocks (only incoming changes, even an
    /// empty list), or <see cref="KVRebaseOutcome.HasUnresolvedConflicts"/> when a manual decision is needed.
    /// </summary>
    public KVRebaseOutcome BeginRebase(KVSnapshot target, IReadOnlyList<KVCommit> missingCommits)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(missingCommits);
        if (this.IsRebasing)
            throw new InvalidOperationException("A rebase is already in progress. Finish or cancel it first.");

        if (missingCommits.Count == 0)
            return KVRebaseOutcome.AlreadyCurrent;

        var theirs = FromCommits(this.Snapshot, missingCommits);
        var result = KVMerge.Merge(this.Snapshot, theirs.Changes, this.Changes);

        // Pre-seed the default (accept-all) merged $items arrays so collection membership reflects both
        // sides' additions and clean deletions. Rejecting an incoming item later edits these.
        foreach (var (path, value) in result.MergedItemArrays)
            this.Changes[path] = value;

        this.RebaseTarget = target.Clone();
        this.Conflicts = result.Conflicts.ToList();
        this.IsRebasing = true;
        return this.HasUnresolvedConflicts ? KVRebaseOutcome.HasUnresolvedConflicts : KVRebaseOutcome.CanAutomerge;
    }

    public void ResolveConflict(string path, KVConflictResolution resolution, KVValue? customValue = null)
    {
        if (!this.IsRebasing)
            throw new InvalidOperationException("No rebase is in progress.");

        var conflict = this.Conflicts.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No conflict at path '{path}'.");

        conflict.Resolve(resolution, customValue);
    }

    /// <summary>
    /// Applies every conflict resolution to the draft, swaps the base to the frozen target and clears the
    /// rebase state. All conflicts must be resolved first.
    /// </summary>
    public void FinishRebase()
    {
        if (!this.IsRebasing)
            throw new InvalidOperationException("No rebase is in progress.");
        if (this.RebaseTarget is null)
            throw new InvalidOperationException("Rebase target is missing.");
        if (this.HasUnresolvedConflicts)
            throw new InvalidOperationException("Cannot finish a rebase while conflicts are unresolved.");

        foreach (var conflict in this.Conflicts)
            this.ApplyResolution(conflict);

        this.Snapshot = this.RebaseTarget;
        this.RebaseTarget = null;
        this.Conflicts = [];
        this.IsRebasing = false;
    }

    /// <summary>Aborts the rebase but keeps the draft changes — the overlay stays on its original (now stale) base.</summary>
    public void CancelRebase()
    {
        if (!this.IsRebasing)
            throw new InvalidOperationException("No rebase is in progress.");

        this.RebaseTarget = null;
        this.Conflicts = [];
        this.IsRebasing = false;
    }

    /// <summary>
    /// Discards all draft changes and resyncs the overlay onto <paramref name="target"/>. This is the
    /// "drop my changes" escape hatch from a rebase, and also the engine behind a general "start over".
    /// </summary>
    public void Reset(KVSnapshot target)
    {
        ArgumentNullException.ThrowIfNull(target);

        this.Changes.Clear();
        this.Snapshot = target.Clone();
        this.RebaseTarget = null;
        this.Conflicts = [];
        this.IsRebasing = false;
    }

    /// <summary>Restores persisted rebase state when an in-progress rebase is reloaded from storage.</summary>
    public void RestoreRebaseState(KVSnapshot target, IEnumerable<KVConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(conflicts);
        this.RebaseTarget = target;
        this.Conflicts = conflicts.ToList();
        this.IsRebasing = true;
    }

    public KVCommit ToCommit(DateTimeOffset timestamp)
    {
        return new KVCommit
        {
            CommitId = Guid.NewGuid(),
            PreviousCommitId = this.BaseCommitId,
            User = this.User,
            Timestamp = timestamp,
            Changes = new KVDictionary(this.Changes)
        };
    }

    // True when the path equals or sits under an inherited root member.
    private bool IsInheritedPath(ReadOnlySpan<char> path)
    {
        foreach (var prefix in this.inheritedPrefixes)
            if (KVPath.IsSameOrDescendant(path, prefix)) return true;
        return false;
    }

    private void GuardWritable(string path)
    {
        if (this.inheritedSource is not null && this.IsInheritedPath(path))
            throw new InvalidOperationException($"Path '{path}' is inherited from the parent and is read-only.");
    }

    // Drops change entries that don't actually differ from the base: redundant upserts (equal value) and
    // tombstones for paths the base has nothing at or under. Makes a net-zero fold canonical (empty).
    private void NormalizeAgainstBase()
    {
        foreach (var key in this.Changes.Keys.ToList())
        {
            var value = this.Changes[key];
            if (value == KVValue.Tombstone)
            {
                if (!this.Snapshot.ContainsPathOrDescendant(key))
                    this.Changes.Remove(key);
            }
            else
            {
                this.Snapshot.TryGet(key, out var baseValue);
                if (KVMerge.ValueEquals(value, baseValue))
                    this.Changes.Remove(key);
            }
        }
    }

    private void ApplyResolution(KVConflict conflict)
    {
        // ── Incoming (non-conflicting) changes ───────────────────────────────────
        if (conflict.IsIncoming)
        {
            switch (conflict.Resolution)
            {
                case KVConflictResolution.Theirs: // accept — let the target show through after the swap.
                    foreach (var key in this.Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, conflict.Path)).ToList())
                        this.Changes.Remove(key);
                    break;

                case KVConflictResolution.Ours: // reject — pin the pre-rebase (base) state as a counter-edit.
                    this.ForceBaseSubtree(conflict.Path);
                    if (conflict.Kind == KVConflictKind.IncomingItem)
                        this.SyncItemsArrayForIncomingReject(conflict);
                    break;

                default:
                    throw new InvalidOperationException($"Incoming change at '{conflict.Path}' is unresolved.");
            }

            return;
        }

        // ── Real conflicts ───────────────────────────────────────────────────────
        switch (conflict.Resolution)
        {
            case KVConflictResolution.Ours:
                // Keep the draft as-is. For a delete/edit conflict that means the tombstone stays.
                if (conflict.Kind == KVConflictKind.Structural)
                {
                    // Replace the whole subtree with ours: the draft's own values stay, but any target leaf
                    // under this path that the draft does NOT set is tombstoned, so the other type's fields
                    // (or dropped collection items) don't bleed through once the base swaps to the target.
                    foreach (var key in this.RebaseTarget!.Data.Keys)
                    {
                        if (!KVPath.IsSameOrDescendant(key, conflict.Path) || this.Changes.ContainsKey(key))
                            continue;
                        this.Changes[key] = KVValue.Tombstone;
                    }
                }

                // For item-level DeleteEdit Ours — no changes needed:
                //   Case A (we deleted, target modified): tombstone stays, $items already excludes the item.
                //   Case B (target deleted, we edited): our field changes stay, $items already includes the item.
                break;

            case KVConflictResolution.Theirs:
                // Drop the draft's change(s) under this path so the target value shows through after the swap.
                foreach (var key in this.Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, conflict.Path)).ToList())
                    this.Changes.Remove(key);

                // For item-level DeleteEdit, also sync the $items membership array.
                if (conflict.Kind == KVConflictKind.DeleteEdit)
                    this.SyncItemsArrayForResolution(conflict);
                break;

            case KVConflictResolution.Custom:
                this.Changes[conflict.Path] = conflict.CustomValue
                    ?? throw new InvalidOperationException($"Custom resolution at '{conflict.Path}' has no value.");
                break;

            default:
                throw new InvalidOperationException($"Conflict at '{conflict.Path}' is unresolved.");
        }
    }

    // Pins the pre-rebase (base = current Snapshot) state over a subtree as a counter-edit, so that after
    // the base swaps to the target the effective value under <paramref name="path"/> stays at base — i.e.
    // the incoming change is rejected. Only actual base↔target differences are written, keeping the draft clean.
    private void ForceBaseSubtree(string path)
    {
        foreach (var key in this.Changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, path)).ToList())
            this.Changes.Remove(key);

        foreach (var key in this.Snapshot.Data.Keys.Where(k => KVPath.IsSameOrDescendant(k, path)))
        {
            this.RebaseTarget!.Data.TryGetValue(key, out var targetValue);
            if (!KVMerge.ValueEquals(this.Snapshot.Data[key], targetValue))
                this.Changes[key] = this.Snapshot.Data[key]; // differs from target — pin base value.
        }

        foreach (var key in this.RebaseTarget!.Data.Keys.Where(k => KVPath.IsSameOrDescendant(k, path)))
        {
            if (!this.Snapshot.Data.ContainsKey(key))
                this.Changes[key] = KVValue.Tombstone; // target-only leaf — remove it so base (absence) wins.
        }
    }

    // $items fixup when an incoming collection-membership change is rejected.
    //   Incoming add  (base empty under item path): reject → remove the id from our merged $items.
    //   Incoming remove (base present under item path): reject → add the id back to our merged $items.
    private void SyncItemsArrayForIncomingReject(KVConflict conflict)
    {
        var parentPath = KVPath.ParentPath(conflict.Path);
        var itemsPath = KVPath.Combine(parentPath, "$items");
        var itemId = KVMerge.LastSegment(conflict.Path);

        // No draft entry means the merge left membership to the target, so start the repair from the target's.
        if (!this.Changes.TryGetValue(itemsPath, out var currentItems))
            this.RebaseTarget!.Data.TryGetValue(itemsPath, out currentItems);
        if (currentItems == KVValue.Tombstone)
            return;

        var ids = KVMerge.ExtractItemIds(currentItems);

        // Decided by base membership rather than by data under the item path: an item can have no fields.
        this.Snapshot.Data.TryGetValue(itemsPath, out var baseItems);
        var wasIncomingAdd = !KVMerge.ExtractItemIds(baseItems).Contains(itemId, System.StringComparer.Ordinal);

        if (wasIncomingAdd)
            ids.Remove(itemId);
        else if (!ids.Contains(itemId, System.StringComparer.Ordinal))
            ids.Add(itemId);

        this.Changes[itemsPath] = KVMerge.BuildItemsValue(ids);
    }

    // Keeps the $items membership array consistent when a Theirs resolution is applied to an
    // item-level DeleteEdit conflict.
    //
    // Case A (OursValue == null — we tombstoned the item, target edited it):
    //   Theirs = restore the item. The Theirs branch already removed our tombstone. We also
    //   need to add the item back into our $items array so the collection shows it.
    //
    // Case B (OursValue != null — target deleted the item, we edited it):
    //   Theirs = accept the deletion. The Theirs branch removed our field edits. We also need
    //   to remove the item from our $items array so the collection no longer shows it.
    private void SyncItemsArrayForResolution(KVConflict conflict)
    {
        var parentPath = KVPath.ParentPath(conflict.Path);
        var itemsPath = KVPath.Combine(parentPath, "$items");
        var itemId = KVMerge.LastSegment(conflict.Path);

        if (!this.Changes.TryGetValue(itemsPath, out var currentItems) || currentItems == KVValue.Tombstone)
            return;

        var ids = KVMerge.ExtractItemIds(currentItems);

        if (conflict.OursValue is null)
        {
            // Case A Theirs: add the item back.
            if (!ids.Contains(itemId, System.StringComparer.Ordinal))
                ids.Add(itemId);
        }
        else
        {
            // Case B Theirs: remove the item.
            ids.Remove(itemId);
        }

        this.Changes[itemsPath] = KVMerge.BuildItemsValue(ids);
    }
}
