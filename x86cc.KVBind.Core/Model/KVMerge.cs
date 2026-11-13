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
    private const string TypeSegment = "$type";

    private const string ItemsSegment = "$items";

    public static KVMergeResult Merge(
        KVSnapshot baseSnapshot,
        IReadOnlyDictionary<string, KVValue> theirs,
        IReadOnlyDictionary<string, KVValue> ours)
        => new MergeRun(baseSnapshot, theirs, ours).Run();

    // ── Helpers ──────────────────────────────────────────────────────────────────

    // Membership ids in array order, first occurrence wins. Order is part of the value: the merge
    // rebuilds $items from these lists, so a set here would make collection order depend on hashing.
    internal static List<string> ExtractItemIds(KVValue? value)
    {
        if (value is null || value == KVValue.Tombstone) return [];
        IEnumerable<string> ids = value.Value switch
        {
            string[] arr => arr,
            object[] arr => arr.OfType<string>(),
            JsonElement je when je.ValueKind == JsonValueKind.Array =>
                je.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0),
            _ => []
        };
        return ids.Distinct(System.StringComparer.Ordinal).ToList();
    }

    internal static KVValue BuildItemsValue(IEnumerable<string> ids) => KVValue.FromObject(ids.ToArray());

    internal static string LastSegment(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    internal static bool ValueEquals(KVValue? a, KVValue? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.Equals(b);
    }

    // One merge invocation: the shared state of the passes, which run in a fixed order (see Run).
    private sealed class MergeRun
    {
        private readonly KVSnapshot baseSnapshot;

        private readonly IReadOnlyDictionary<string, KVValue> theirs;

        // The draft change-set ("ours").
        private readonly IReadOnlyDictionary<string, KVValue> changes;

        // "Theirs" resolved against the base: an overlay whose TryGet/IsRemoved give the upstream-effective
        // value at any path (their override, or the base value, or absent when they deleted it).
        private readonly KVOverlay theirsOverlay;

        // The set of leaf paths upstream actually changed vs base.
        private readonly HashSet<string> mainChanged;

        // Tracks every mainChanged path already represented by some entry (conflict or grouped incoming),
        // so the trailing scalar-incoming pass doesn't double-report.
        private readonly HashSet<string> consumed = new(System.StringComparer.Ordinal);

        private readonly List<KVConflict> conflicts = [];

        private readonly List<KVConflict> incoming = [];

        private readonly Dictionary<string, KVValue> mergedItemArrays = new(System.StringComparer.Ordinal);

        private readonly List<string> structuralNodes = [];

        public MergeRun(
            KVSnapshot baseSnapshot,
            IReadOnlyDictionary<string, KVValue> theirs,
            IReadOnlyDictionary<string, KVValue> ours)
        {
            this.baseSnapshot = baseSnapshot;
            this.theirs = theirs;
            this.changes = ours;
            this.theirsOverlay = KVOverlay.Create(baseSnapshot, "upstream");
            this.theirsOverlay.Changes = new KVDictionary(theirs);
            this.mainChanged = ComputeMainChanged(baseSnapshot, theirs);
        }

        public KVMergeResult Run()
        {
            this.DetectTypeConflicts();
            this.DetectIncomingTypeChanges();
            this.MergeCollectionMemberships();
            this.DetectTombstoneConflicts();
            this.DetectValueConflicts();
            this.CollectRemainingIncoming();

            // Incoming entries default to "accept".
            foreach (var entry in this.incoming)
                entry.Resolution = KVConflictResolution.Theirs;

            var all = this.conflicts.Concat(this.incoming)
                .OrderBy(c => c.Path, System.StringComparer.Ordinal)
                .ToList();

            return new KVMergeResult(all, this.mergedItemArrays);
        }

        // Upserts that differ from base; prefix tombstones expanded against the base leaves they remove.
        private static HashSet<string> ComputeMainChanged(KVSnapshot baseSnapshot, IReadOnlyDictionary<string, KVValue> theirs)
        {
            var changed = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var (k, v) in theirs)
            {
                if (v == KVValue.Tombstone)
                {
                    foreach (var baseKey in baseSnapshot.Data.Keys)
                    {
                        if (KVPath.IsSameOrDescendant(baseKey, k))
                            changed.Add(baseKey);
                    }
                }
                else
                {
                    baseSnapshot.Data.TryGetValue(k, out var bv);
                    if (!ValueEquals(v, bv)) changed.Add(k);
                }
            }

            return changed;
        }

        // Build the default (accept-all) merged membership array:
        //   target order, minus items ours cleanly deleted, then ours' additions in ours' order, then Case B
        //   items (tentative) in base order.
        private static List<string> BuildMergedIds(
            List<string> targetIds,
            HashSet<string> removedByOurs,
            List<string> addedByOurs,
            List<string> caseBConflicted)
        {
            var mergedIds = new List<string>();
            var mergedSet = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var id in targetIds)
            {
                if (removedByOurs.Contains(id) && !caseBConflicted.Contains(id, System.StringComparer.Ordinal)) continue;
                if (mergedSet.Add(id)) mergedIds.Add(id);
            }

            foreach (var id in addedByOurs.Concat(caseBConflicted))
            {
                if (mergedSet.Add(id)) mergedIds.Add(id);
            }

            return mergedIds;
        }

        private static bool IsReservedLeaf(string key, string segment) =>
            string.Equals(key, segment, System.StringComparison.Ordinal)
            || key.EndsWith("/" + segment, System.StringComparison.Ordinal);

        private static string ParentPath(string key)
        {
            var slash = key.LastIndexOf('/');
            return slash < 0 ? string.Empty : key[..slash];
        }

        // Upstream-effective value at a path (false when upstream deleted it).
        private bool TheirsGet(string path, out KVValue? value) => this.theirsOverlay.TryGet(path, out value);

        private bool OverlayTouchedUnder(string path) =>
            this.changes.Keys.Any(k => KVPath.IsSameOrDescendant(k, path) && this.changes[k] != null);

        private bool CoveredByStructuralNode(string path) =>
            this.structuralNodes.Any(n => KVPath.IsSameOrDescendant(path, n));

        private void ConsumeMainChangedUnder(string path)
        {
            foreach (var p in this.mainChanged.Where(p => KVPath.IsSameOrDescendant(p, path)).ToList())
                this.consumed.Add(p);
        }

        // ── Pass 0 — $type conflicts (both sides chose a different type) ─────────────
        private void DetectTypeConflicts()
        {
            foreach (var key in this.mainChanged.Where(k => IsReservedLeaf(k, TypeSegment)))
            {
                var nodePath = ParentPath(key);
                if (string.IsNullOrEmpty(nodePath)) continue;
                if (!this.changes.TryGetValue(key, out var oursType) || oursType == KVValue.Tombstone) continue;

                this.TheirsGet(key, out var mainType);
                if (ValueEquals(oursType, mainType)) continue;

                this.baseSnapshot.Data.TryGetValue(key, out var baseType);
                this.conflicts.Add(new KVConflict
                {
                    Path = nodePath, Kind = KVConflictKind.Structural,
                    BaseValue = baseType, MainValue = mainType, OursValue = oursType,
                });
                this.structuralNodes.Add(nodePath);
            }

            foreach (var p in this.mainChanged.Where(this.CoveredByStructuralNode).ToList())
                this.consumed.Add(p);
        }

        // ── Pass 0b — incoming $type change (target changed a node the draft left untouched) ──
        private void DetectIncomingTypeChanges()
        {
            foreach (var key in this.mainChanged.Where(k => IsReservedLeaf(k, TypeSegment)))
            {
                if (this.consumed.Contains(key)) continue;
                var nodePath = ParentPath(key);
                if (string.IsNullOrEmpty(nodePath)) continue;
                if (this.OverlayTouchedUnder(nodePath)) continue; // draft touched it — leave to leaf-level handling.

                this.baseSnapshot.Data.TryGetValue(key, out var baseType);
                this.TheirsGet(key, out var mainType);
                this.incoming.Add(new KVConflict
                {
                    Path = nodePath, Kind = KVConflictKind.Incoming,
                    BaseValue = baseType, MainValue = mainType, OursValue = null,
                });
                this.ConsumeMainChangedUnder(nodePath);
            }
        }

        // ── Pass 1 — collection membership ($items) ──────────────────────────────────
        private void MergeCollectionMemberships()
        {
            var itemsKeys = this.baseSnapshot.Data.Keys
                .Concat(this.theirs.Keys)
                .Concat(this.changes.Keys)
                .Where(k => IsReservedLeaf(k, ItemsSegment))
                .Distinct(System.StringComparer.Ordinal);

            foreach (var key in itemsKeys)
                this.MergeMembership(key);
        }

        private void MergeMembership(string key)
        {
            if (this.CoveredByStructuralNode(key)) return;
            if (this.changes.TryGetValue(key, out var oursTomb) && oursTomb == KVValue.Tombstone) return;

            var collectionPath = ParentPath(key);
            this.TheirsGet(key, out var theirsItemsValue);
            var baseIds = ExtractItemIds(this.baseSnapshot.Data.GetValueOrDefault(key));
            var targetIds = ExtractItemIds(theirsItemsValue);

            // A draft that never touched this membership still has the base items, not an empty collection.
            var oursTouchedItems = this.changes.TryGetValue(key, out var oursItemsValue);
            var ourIds = oursTouchedItems ? ExtractItemIds(oursItemsValue) : baseIds;

            // Except keeps the order of its first sequence, so these lists carry array order; the sets are lookups only.
            var addedByOurs = ourIds.Except(baseIds, System.StringComparer.Ordinal).ToList();
            var removedByTarget = baseIds.Except(targetIds, System.StringComparer.Ordinal).ToList();
            var addedByTarget = targetIds.Except(baseIds, System.StringComparer.Ordinal);
            var addedByOursSet = addedByOurs.ToHashSet(System.StringComparer.Ordinal);
            var removedByOurs = baseIds.Except(ourIds, System.StringComparer.Ordinal).ToHashSet(System.StringComparer.Ordinal);

            var caseBConflicted = this.AddDeletedItemConflicts(key, collectionPath, removedByTarget, removedByOurs);
            this.AddIncomingItemAdditions(collectionPath, addedByTarget, addedByOursSet);
            this.AddIncomingItemRemovals(collectionPath, removedByTarget, removedByOurs, caseBConflicted);

            var mergedIds = BuildMergedIds(targetIds, removedByOurs, addedByOurs, caseBConflicted);

            this.consumed.Add(key);

            // A draft that edited $items always gets the merged array (it replaces the draft's own). An
            // untouched one is only seeded when the merge differs from the target; otherwise the target
            // shows through after the base swap and the draft stays free of a redundant $items entry.
            if (oursTouchedItems || !mergedIds.SequenceEqual(targetIds, System.StringComparer.Ordinal))
                this.mergedItemArrays[key] = BuildItemsValue(mergedIds);
        }

        // Conflict: target deleted an item ours edited (Case B). Returns the conflicted item ids.
        private List<string> AddDeletedItemConflicts(
            string key,
            string collectionPath,
            List<string> removedByTarget,
            HashSet<string> removedByOurs)
        {
            var caseBConflicted = new List<string>();
            foreach (var id in removedByTarget)
            {
                if (removedByOurs.Contains(id)) continue;                      // both deleted — clean.
                if (!this.ItemEditedByOurs(key, collectionPath, id)) continue; // target deleted, ours untouched — incoming remove.

                caseBConflicted.Add(id);
                var itemPath = KVPath.Combine(collectionPath, id);
                foreach (var sub in this.changes.Keys.Where(k => KVPath.IsSameOrDescendant(k, itemPath)))
                    this.consumed.Add(sub);
                this.conflicts.Add(new KVConflict
                {
                    Path = itemPath, Kind = KVConflictKind.DeleteEdit,
                    BaseValue = null, MainValue = null, OursValue = KVValue.FromObject(true),
                });
            }

            return caseBConflicted;
        }

        private bool ItemEditedByOurs(string key, string collectionPath, string id) =>
            this.changes.Keys.Any(k =>
                !string.Equals(k, key, System.StringComparison.Ordinal) &&
                KVPath.IsSameOrDescendant(k, KVPath.Combine(collectionPath, id)));

        // Incoming: target added an item ours did not.
        private void AddIncomingItemAdditions(string collectionPath, IEnumerable<string> addedByTarget, HashSet<string> addedByOursSet)
        {
            foreach (var id in addedByTarget)
            {
                if (addedByOursSet.Contains(id)) continue;        // both added the same id — degenerate, treat as ours.
                var itemPath = KVPath.Combine(collectionPath, id);
                if (this.OverlayTouchedUnder(itemPath)) continue; // ours also touched — not a clean incoming add.

                this.incoming.Add(new KVConflict
                {
                    Path = itemPath, Kind = KVConflictKind.IncomingItem,
                    BaseValue = null, MainValue = KVValue.FromObject(true), OursValue = null,
                });
                this.ConsumeMainChangedUnder(itemPath);
            }
        }

        // Incoming: target removed an item ours did not touch.
        private void AddIncomingItemRemovals(
            string collectionPath,
            List<string> removedByTarget,
            HashSet<string> removedByOurs,
            List<string> caseBConflicted)
        {
            foreach (var id in removedByTarget)
            {
                if (removedByOurs.Contains(id)) continue;
                if (caseBConflicted.Contains(id, System.StringComparer.Ordinal)) continue;
                var itemPath = KVPath.Combine(collectionPath, id);

                this.incoming.Add(new KVConflict
                {
                    Path = itemPath, Kind = KVConflictKind.IncomingItem,
                    BaseValue = KVValue.FromObject(true), MainValue = null, OursValue = null,
                });
                this.ConsumeMainChangedUnder(itemPath);
            }
        }

        // ── Pass 2 — tombstone (delete/edit) conflicts ───────────────────────────────
        private void DetectTombstoneConflicts()
        {
            var tombstones = this.changes.Where(p => p.Value == KVValue.Tombstone).Select(p => p.Key).ToList();
            foreach (var tombstone in tombstones)
            {
                if (this.CoveredByStructuralNode(tombstone)) continue;

                var mainUnder = this.mainChanged.Where(path => KVPath.IsSameOrDescendant(path, tombstone)).ToList();
                foreach (var path in mainUnder) this.consumed.Add(path);

                // Only a conflict when upstream actively KEPT at least one path under our deletion.
                // If everything under it is also absent upstream, both sides deleted the same thing — clean.
                var mainActiveUnder = mainUnder.Where(path => this.TheirsGet(path, out _)).ToList();
                if (mainActiveUnder.Count == 0) continue;

                this.baseSnapshot.Data.TryGetValue(tombstone, out var baseLeaf);
                this.TheirsGet(tombstone, out var mainLeaf);
                this.conflicts.Add(new KVConflict
                {
                    Path = tombstone, Kind = KVConflictKind.DeleteEdit,
                    BaseValue = baseLeaf, MainValue = mainLeaf, OursValue = null,
                });
            }
        }

        // ── Pass 3 — leaf value conflicts & convergent resyncs ───────────────────────
        private void DetectValueConflicts()
        {
            foreach (var path in this.mainChanged)
            {
                if (this.consumed.Contains(path)) continue;
                if (this.CoveredByStructuralNode(path)) continue;
                if (!this.changes.TryGetValue(path, out var oursValue) || oursValue == KVValue.Tombstone)
                    continue; // overlay didn't set this leaf — handled by the incoming pass below.

                this.TheirsGet(path, out var mainValue);
                this.baseSnapshot.Data.TryGetValue(path, out var baseValue);

                // Both sides converging to the same value is not a conflict, but is surfaced as an incoming
                // "resync" so the user sees it and can still reject back to base.
                var converged = ValueEquals(oursValue, mainValue);
                (converged ? this.incoming : this.conflicts).Add(new KVConflict
                {
                    Path = path, Kind = converged ? KVConflictKind.Incoming : KVConflictKind.Value,
                    BaseValue = baseValue, MainValue = mainValue, OursValue = oursValue,
                });
                this.consumed.Add(path);
            }
        }

        // ── Pass 4 — remaining incoming scalar changes (target changed, draft untouched) ──
        private void CollectRemainingIncoming()
        {
            foreach (var path in this.mainChanged)
            {
                if (this.consumed.Contains(path)) continue;
                if (this.changes.ContainsKey(path)) continue; // overlay touched it — already handled above.

                this.baseSnapshot.Data.TryGetValue(path, out var baseValue);
                this.TheirsGet(path, out var mainValue);
                this.incoming.Add(new KVConflict
                {
                    Path = path, Kind = KVConflictKind.Incoming,
                    BaseValue = baseValue, MainValue = mainValue, OursValue = null,
                });
            }
        }
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
