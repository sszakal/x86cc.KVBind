using System.Collections.Generic;
using System.Linq;

namespace x86cc.KVBind.Core.Model;

/// <summary>
/// Three-way merge between a common base snapshot (V1), a rebase target snapshot (V2 / main)
/// and an overlay's draft changes (ours). Produces the set of conflicts a human must resolve.
/// </summary>
/// <remarks>
/// The overlay is a single working set (like <c>git stash</c>), not a branch of commits, so a rebase
/// here is a stash-pop style three-way merge of the net V1→V2 difference against the draft — not a
/// commit replay. Snapshots are folds of their commits, so the net diff carries everything that matters
/// for conflict detection; intermediate commits add nothing.
/// </remarks>
public static class KVMerge
{
    // Reserved leaf segments that are structural rather than user-editable: the polymorphic discriminator
    // and the collection membership array. Neither can be merged leaf-by-leaf.
    private const string TypeSegment = "$type";
    private const string ItemsSegment = "$items";

    /// <summary>
    /// Computes the conflicts between <paramref name="baseSnapshot"/> (V1), <paramref name="targetSnapshot"/> (V2)
    /// and the overlay <paramref name="changes"/>. A path is conflicting only when BOTH the target and the
    /// overlay changed it relative to the base, to different values. Non-overlapping target changes are
    /// auto-merged on finish (they show through once the overlay's base is swapped) and produce no conflict.
    /// </summary>
    public static IReadOnlyList<KVConflict> ComputeConflicts(
        KVSnapshot baseSnapshot,
        KVSnapshot targetSnapshot,
        IReadOnlyDictionary<string, KVValue> changes)
    {
        var conflicts = new List<KVConflict>();

        // Net V1→V2 diff at leaf granularity: every key whose value differs (added, removed or modified).
        var mainChanged = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var key in baseSnapshot.Data.Keys.Concat(targetSnapshot.Data.Keys))
        {
            baseSnapshot.Data.TryGetValue(key, out var baseValue);
            targetSnapshot.Data.TryGetValue(key, out var mainValue);
            if (!ValueEquals(baseValue, mainValue))
                mainChanged.Add(key);
        }

        // Pass 0 — structural conflicts. A polymorphic node whose $type both sides changed to different
        // types, and a collection whose $items membership array both sides changed, cannot be merged
        // leaf-by-leaf. Each collapses to a single whole-subtree Ours/Theirs decision. For a $type clash
        // the conflict is recorded at the node path and every leaf under it is suppressed, so the user
        // never sees granular field diffs across two incompatible shapes.
        var structuralNodes = new List<string>();          // $type node paths — suppress descendants
        var structuralLeaves = new HashSet<string>(System.StringComparer.Ordinal); // $items array paths

        foreach (var key in mainChanged)
        {
            if (!IsReservedLeaf(key, TypeSegment))
                continue;

            var nodePath = ParentPath(key);
            if (string.IsNullOrEmpty(nodePath))
                continue; // never collapse the whole aggregate.

            if (!changes.TryGetValue(key, out var oursType) || oursType == KVValue.Tombstone)
                continue; // overlay did not pick a different type — target's type shows through cleanly.

            targetSnapshot.Data.TryGetValue(key, out var mainType);
            if (ValueEquals(oursType, mainType))
                continue; // both sides chose the same type — fall through to leaf merging.

            baseSnapshot.Data.TryGetValue(key, out var baseType);
            conflicts.Add(new KVConflict
            {
                Path = nodePath,
                Kind = KVConflictKind.Structural,
                BaseValue = baseType,
                MainValue = mainType,
                OursValue = oursType,
            });
            structuralNodes.Add(nodePath);
        }

        bool CoveredByStructuralNode(string path) =>
            structuralNodes.Any(node => KVPath.IsSameOrDescendant(path, node));

        foreach (var key in mainChanged)
        {
            if (!IsReservedLeaf(key, ItemsSegment) || CoveredByStructuralNode(key))
                continue;

            if (!changes.TryGetValue(key, out var oursItems) || oursItems == KVValue.Tombstone)
                continue;

            targetSnapshot.Data.TryGetValue(key, out var mainItems);
            if (ValueEquals(oursItems, mainItems))
                continue;

            baseSnapshot.Data.TryGetValue(key, out var baseItems);
            conflicts.Add(new KVConflict
            {
                Path = key,
                Kind = KVConflictKind.Structural,
                BaseValue = baseItems,
                MainValue = mainItems,
                OursValue = oursItems,
            });
            structuralLeaves.Add(key);
        }

        // Overlay deletions (tombstones). A tombstone at path t deletes t and its whole subtree.
        var tombstones = changes
            .Where(pair => pair.Value == KVValue.Tombstone)
            .Select(pair => pair.Key)
            .ToList();

        // Pass 1 — delete/edit conflicts. If the overlay deleted a subtree that the target also changed,
        // that is a structural conflict recorded once at the tombstone path.
        var coveredByTombstone = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var tombstone in tombstones)
        {
            if (CoveredByStructuralNode(tombstone))
                continue; // the whole node is being resolved structurally — no separate delete/edit conflict.

            var mainUnder = mainChanged
                .Where(path => KVPath.IsSameOrDescendant(path, tombstone))
                .ToList();

            foreach (var path in mainUnder)
                coveredByTombstone.Add(path);

            if (mainUnder.Count == 0)
                continue; // overlay deleted something the target left alone — clean, keep the deletion.

            baseSnapshot.Data.TryGetValue(tombstone, out var baseLeaf);
            targetSnapshot.Data.TryGetValue(tombstone, out var mainLeaf);

            conflicts.Add(new KVConflict
            {
                Path = tombstone,
                Kind = KVConflictKind.DeleteEdit,
                BaseValue = baseLeaf,
                MainValue = mainLeaf, // null for a subtree (structural) — leaf deletions carry the upstream value
                OursValue = null,     // the overlay deleted it
            });
        }

        // Pass 2 — value conflicts. The overlay set a value at a path the target also changed, to a
        // different value. Paths already covered by a tombstone conflict are skipped.
        foreach (var path in mainChanged)
        {
            if (coveredByTombstone.Contains(path))
                continue;

            if (CoveredByStructuralNode(path) || structuralLeaves.Contains(path))
                continue; // already recorded as a whole-subtree structural conflict.

            if (!changes.TryGetValue(path, out var oursValue) || oursValue == KVValue.Tombstone)
                continue; // overlay did not set this leaf — target change shows through cleanly.

            targetSnapshot.Data.TryGetValue(path, out var mainValue);
            if (ValueEquals(oursValue, mainValue))
                continue; // both sides arrived at the same value — no conflict.

            baseSnapshot.Data.TryGetValue(path, out var baseValue);

            conflicts.Add(new KVConflict
            {
                Path = path,
                Kind = KVConflictKind.Value,
                BaseValue = baseValue,
                MainValue = mainValue,
                OursValue = oursValue,
            });
        }

        return conflicts
            .OrderBy(conflict => conflict.Path, System.StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>True when <paramref name="key"/>'s final segment is the given reserved segment (e.g. <c>$type</c>, <c>$items</c>).</summary>
    private static bool IsReservedLeaf(string key, string segment) =>
        string.Equals(key, segment, System.StringComparison.Ordinal)
        || key.EndsWith("/" + segment, System.StringComparison.Ordinal);

    /// <summary>The path of the parent node — everything before the final segment. Empty for a top-level key.</summary>
    private static string ParentPath(string key)
    {
        var slash = key.LastIndexOf('/');
        return slash < 0 ? string.Empty : key[..slash];
    }

    /// <summary>Equality where <c>null</c> means "absent". Snapshots never hold tombstones, so this is a plain value compare.</summary>
    internal static bool ValueEquals(KVValue? a, KVValue? b)
    {
        if (a is null || b is null)
            return a is null && b is null;
        return a.Equals(b);
    }
}
