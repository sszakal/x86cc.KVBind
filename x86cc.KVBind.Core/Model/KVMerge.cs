using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace x86cc.KVBind.Core.Model;

/// <summary>
/// Three-way merge of two change-sets over a common base snapshot: the draft (<c>ours</c>) and the
/// folded upstream commits (<c>theirs</c>). Both are <see cref="KVOverlay.Changes"/>-shaped maps over
/// the same base, so a rebase is the symmetric merge of two overlays sharing a base. Produces a review
/// list — real conflicts and non-conflicting incoming changes.
/// </summary>
/// <remarks>
/// The diff is driven entirely by the change-sets, bounded to the paths either side touched; the only
/// place the full base is consulted is to expand a prefix tombstone (e.g. <c>Remove("Claimant")</c>)
/// into the concrete leaves it deletes.
///
/// The review list has two classes of entry:
/// <list type="bullet">
///   <item><b>Conflicts</b> (Value / DeleteEdit / Structural): both sides changed the same thing. No default — the user must choose.</item>
///   <item><b>Incoming</b> (Incoming / IncomingItem): upstream changed something the draft did not touch. Defaults to accept; rejectable.</item>
/// </list>
/// </remarks>
public static class KVMerge
{
    private const string TypeSegment  = "$type";
    private const string ItemsSegment = "$items";

    public static KVMergeResult Merge(
        KVSnapshot baseSnapshot,
        IReadOnlyDictionary<string, KVValue> theirs,
        IReadOnlyDictionary<string, KVValue> ours)
    {
        var conflicts        = new List<KVConflict>();
        var incoming         = new List<KVConflict>();
        var mergedItemArrays = new Dictionary<string, KVValue>(System.StringComparer.Ordinal);

        var changes = ours; // the passes below read the draft change-set as "changes".

        // "Theirs" resolved against the base: an overlay whose TryGet/IsRemoved give the upstream-effective
        // value at any path (their override, or the base value, or absent when they deleted it).
        var theirsOverlay = KVOverlay.Create(baseSnapshot, "upstream");
        theirsOverlay.Changes = new Dictionary<string, KVValue>(theirs, System.StringComparer.Ordinal);

        // The set of leaf paths upstream actually changed vs base (replaces the old full-snapshot scan).
        // Upserts that differ from base; prefix tombstones expanded against the base leaves they remove.
        var mainChanged = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var (k, v) in theirs)
        {
            if (v == KVValue.Tombstone)
            {
                foreach (var baseKey in baseSnapshot.Data.Keys)
                    if (KVPath.IsSameOrDescendant(baseKey, k))
                        mainChanged.Add(baseKey);
            }
            else
            {
                baseSnapshot.Data.TryGetValue(k, out var bv);
                if (!ValueEquals(v, bv)) mainChanged.Add(k);
            }
        }

        // Upstream-effective value at a path (false when upstream deleted it).
        bool TheirsGet(string path, out KVValue? value) => theirsOverlay.TryGet(path, out value);

        // Tracks every mainChanged path already represented by some entry (conflict or grouped incoming),
        // so the trailing scalar-incoming pass doesn't double-report.
        var consumed = new HashSet<string>(System.StringComparer.Ordinal);

        bool OverlayTouchedUnder(string path) =>
            changes.Keys.Any(k => KVPath.IsSameOrDescendant(k, path) && changes[k] != null);

        // ── Pass 0 — $type conflicts (both sides chose a different type) ─────────────
        var structuralNodes = new List<string>();
        foreach (var key in mainChanged.Where(k => IsReservedLeaf(k, TypeSegment)))
        {
            var nodePath = ParentPath(key);
            if (string.IsNullOrEmpty(nodePath)) continue;
            if (!changes.TryGetValue(key, out var oursType) || oursType == KVValue.Tombstone) continue;

            TheirsGet(key, out var mainType);
            if (ValueEquals(oursType, mainType)) continue;

            baseSnapshot.Data.TryGetValue(key, out var baseType);
            conflicts.Add(new KVConflict
            {
                Path = nodePath, Kind = KVConflictKind.Structural,
                BaseValue = baseType, MainValue = mainType, OursValue = oursType,
            });
            structuralNodes.Add(nodePath);
        }
        bool CoveredByStructuralNode(string path) =>
            structuralNodes.Any(n => KVPath.IsSameOrDescendant(path, n));
        foreach (var p in mainChanged.Where(CoveredByStructuralNode).ToList())
            consumed.Add(p);

        // ── Pass 0b — incoming $type change (target changed a node the draft left untouched) ──
        foreach (var key in mainChanged.Where(k => IsReservedLeaf(k, TypeSegment)))
        {
            if (consumed.Contains(key)) continue;
            var nodePath = ParentPath(key);
            if (string.IsNullOrEmpty(nodePath)) continue;
            if (OverlayTouchedUnder(nodePath)) continue; // draft touched it — leave to leaf-level handling.

            baseSnapshot.Data.TryGetValue(key, out var baseType);
            TheirsGet(key, out var mainType);
            incoming.Add(new KVConflict
            {
                Path = nodePath, Kind = KVConflictKind.Incoming,
                BaseValue = baseType, MainValue = mainType, OursValue = null,
            });
            foreach (var p in mainChanged.Where(p => KVPath.IsSameOrDescendant(p, nodePath)).ToList())
                consumed.Add(p);
        }

        // ── Pass 1 — collection membership ($items) ──────────────────────────────────
        var itemsKeys = baseSnapshot.Data.Keys
            .Concat(theirs.Keys)
            .Concat(changes.Keys)
            .Where(k => IsReservedLeaf(k, ItemsSegment))
            .Distinct(System.StringComparer.Ordinal);

        foreach (var key in itemsKeys)
        {
            if (CoveredByStructuralNode(key)) continue;
            if (changes.TryGetValue(key, out var oursTomb) && oursTomb == KVValue.Tombstone) continue;

            var collectionPath = ParentPath(key);
            TheirsGet(key, out var theirsItemsValue);
            var baseIds   = ExtractItemIds(baseSnapshot.Data.GetValueOrDefault(key));
            var targetIds = ExtractItemIds(theirsItemsValue);
            var ourIds    = ExtractItemIds(changes.GetValueOrDefault(key));

            var addedByOurs     = ourIds.Except(baseIds, System.StringComparer.Ordinal).ToHashSet(System.StringComparer.Ordinal);
            var removedByOurs   = baseIds.Except(ourIds, System.StringComparer.Ordinal).ToHashSet(System.StringComparer.Ordinal);
            var addedByTarget   = targetIds.Except(baseIds, System.StringComparer.Ordinal).ToHashSet(System.StringComparer.Ordinal);
            var removedByTarget = baseIds.Except(targetIds, System.StringComparer.Ordinal).ToHashSet(System.StringComparer.Ordinal);

            bool ItemEditedByOurs(string id) =>
                changes.Keys.Any(k =>
                    !string.Equals(k, key, System.StringComparison.Ordinal) &&
                    KVPath.IsSameOrDescendant(k, KVPath.Combine(collectionPath, id)));

            var caseBConflicted = new HashSet<string>(System.StringComparer.Ordinal);

            // Conflict: target deleted an item ours edited (Case B).
            foreach (var id in removedByTarget)
            {
                if (removedByOurs.Contains(id)) continue;   // both deleted — clean.
                if (!ItemEditedByOurs(id)) continue;        // target deleted, ours untouched — incoming remove (below).

                caseBConflicted.Add(id);
                var itemPath = KVPath.Combine(collectionPath, id);
                foreach (var sub in changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, itemPath)))
                    consumed.Add(sub);
                conflicts.Add(new KVConflict
                {
                    Path = itemPath, Kind = KVConflictKind.DeleteEdit,
                    BaseValue = null, MainValue = null, OursValue = KVValue.FromObject(true),
                });
            }

            // Incoming: target added an item ours did not.
            foreach (var id in addedByTarget)
            {
                if (addedByOurs.Contains(id)) continue;      // both added the same id — degenerate, treat as ours.
                var itemPath = KVPath.Combine(collectionPath, id);
                if (OverlayTouchedUnder(itemPath)) continue; // ours also touched — not a clean incoming add.

                incoming.Add(new KVConflict
                {
                    Path = itemPath, Kind = KVConflictKind.IncomingItem,
                    BaseValue = null, MainValue = KVValue.FromObject(true), OursValue = null,
                });
                foreach (var p in mainChanged.Where(p => KVPath.IsSameOrDescendant(p, itemPath)).ToList())
                    consumed.Add(p);
            }

            // Incoming: target removed an item ours did not touch.
            foreach (var id in removedByTarget)
            {
                if (removedByOurs.Contains(id)) continue;
                if (caseBConflicted.Contains(id)) continue;
                var itemPath = KVPath.Combine(collectionPath, id);

                incoming.Add(new KVConflict
                {
                    Path = itemPath, Kind = KVConflictKind.IncomingItem,
                    BaseValue = KVValue.FromObject(true), MainValue = null, OursValue = null,
                });
                foreach (var p in mainChanged.Where(p => KVPath.IsSameOrDescendant(p, itemPath)).ToList())
                    consumed.Add(p);
            }

            // Build the default (accept-all) merged membership array:
            //   target order, minus items ours cleanly deleted, plus ours' additions, plus Case B items (tentative).
            var mergedIds = new List<string>();
            foreach (var id in targetIds)
            {
                if (removedByOurs.Contains(id) && !caseBConflicted.Contains(id)) continue;
                mergedIds.Add(id);
            }
            foreach (var id in addedByOurs)
                if (!mergedIds.Contains(id, System.StringComparer.Ordinal)) mergedIds.Add(id);
            foreach (var id in caseBConflicted)
                if (!mergedIds.Contains(id, System.StringComparer.Ordinal)) mergedIds.Add(id);

            consumed.Add(key);
            mergedItemArrays[key] = BuildItemsValue(mergedIds);
        }

        // ── Pass 2 — tombstone (delete/edit) conflicts ───────────────────────────────
        var tombstones = changes.Where(p => p.Value == KVValue.Tombstone).Select(p => p.Key).ToList();
        foreach (var tombstone in tombstones)
        {
            if (CoveredByStructuralNode(tombstone)) continue;

            var mainUnder = mainChanged.Where(path => KVPath.IsSameOrDescendant(path, tombstone)).ToList();
            foreach (var path in mainUnder) consumed.Add(path);

            // Only a conflict when upstream actively KEPT at least one path under our deletion.
            // If everything under it is also absent upstream, both sides deleted the same thing — clean.
            var mainActiveUnder = mainUnder.Where(path => TheirsGet(path, out _)).ToList();
            if (mainActiveUnder.Count == 0) continue;

            baseSnapshot.Data.TryGetValue(tombstone, out var baseLeaf);
            TheirsGet(tombstone, out var mainLeaf);
            conflicts.Add(new KVConflict
            {
                Path = tombstone, Kind = KVConflictKind.DeleteEdit,
                BaseValue = baseLeaf, MainValue = mainLeaf, OursValue = null,
            });
        }

        // ── Pass 3 — leaf value conflicts & convergent resyncs ───────────────────────
        foreach (var path in mainChanged)
        {
            if (consumed.Contains(path)) continue;
            if (CoveredByStructuralNode(path)) continue;
            if (!changes.TryGetValue(path, out var oursValue) || oursValue == KVValue.Tombstone)
                continue; // overlay didn't set this leaf — handled by the incoming pass below.

            TheirsGet(path, out var mainValue);
            baseSnapshot.Data.TryGetValue(path, out var baseValue);

            if (ValueEquals(oursValue, mainValue))
            {
                // Both sides converged to the same value — not a conflict, but surfaced as an incoming
                // "resync" so the user sees it and can still reject back to base.
                incoming.Add(new KVConflict
                {
                    Path = path, Kind = KVConflictKind.Incoming,
                    BaseValue = baseValue, MainValue = mainValue, OursValue = oursValue,
                });
                consumed.Add(path);
                continue;
            }

            conflicts.Add(new KVConflict
            {
                Path = path, Kind = KVConflictKind.Value,
                BaseValue = baseValue, MainValue = mainValue, OursValue = oursValue,
            });
            consumed.Add(path);
        }

        // ── Pass 4 — remaining incoming scalar changes (target changed, draft untouched) ──
        foreach (var path in mainChanged)
        {
            if (consumed.Contains(path)) continue;
            if (changes.ContainsKey(path)) continue; // overlay touched it — already handled above.

            baseSnapshot.Data.TryGetValue(path, out var baseValue);
            TheirsGet(path, out var mainValue);
            incoming.Add(new KVConflict
            {
                Path = path, Kind = KVConflictKind.Incoming,
                BaseValue = baseValue, MainValue = mainValue, OursValue = null,
            });
        }

        // Incoming entries default to "accept".
        foreach (var entry in incoming)
            entry.Resolution = KVConflictResolution.Theirs;

        var all = conflicts.Concat(incoming)
            .OrderBy(c => c.Path, System.StringComparer.Ordinal)
            .ToList();

        return new KVMergeResult(all, mergedItemArrays);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    internal static HashSet<string> ExtractItemIds(KVValue? value)
    {
        if (value is null || value == KVValue.Tombstone) return [];
        return value.Value switch
        {
            string[] arr => new HashSet<string>(arr, System.StringComparer.Ordinal),
            object[] arr => new HashSet<string>(arr.OfType<string>(), System.StringComparer.Ordinal),
            JsonElement je when je.ValueKind == JsonValueKind.Array =>
                new HashSet<string>(
                    je.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0),
                    System.StringComparer.Ordinal),
            _ => []
        };
    }

    internal static KVValue BuildItemsValue(IEnumerable<string> ids) => KVValue.FromObject(ids.ToArray());

    internal static string LastSegment(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static bool IsReservedLeaf(string key, string segment) =>
        string.Equals(key, segment, System.StringComparison.Ordinal)
        || key.EndsWith("/" + segment, System.StringComparison.Ordinal);

    private static string ParentPath(string key)
    {
        var slash = key.LastIndexOf('/');
        return slash < 0 ? string.Empty : key[..slash];
    }

    internal static bool ValueEquals(KVValue? a, KVValue? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.Equals(b);
    }
}

/// <summary>The result of <see cref="KVMerge.Merge"/>.</summary>
public sealed class KVMergeResult(
    IReadOnlyList<KVConflict> conflicts,
    IReadOnlyDictionary<string, KVValue> mergedItemArrays)
{
    /// <summary>The full review list — conflicts (need a decision) and incoming changes (default-accepted).</summary>
    public IReadOnlyList<KVConflict> Conflicts { get; } = conflicts;

    /// <summary>Auto-computed default (accept-all) membership arrays keyed by their $items path.</summary>
    public IReadOnlyDictionary<string, KVValue> MergedItemArrays { get; } = mergedItemArrays;
}
