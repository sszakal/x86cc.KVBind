using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

// Records what one KVRootNode.Patch call changed, from the change notifications every write already raises
// (the ones change reactions run on), so a patch's change set covers its operations and the reactions they
// triggered, and costs as much as the patch touched rather than a walk of the whole aggregate.
internal sealed class KVPatchChangeRecorder
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly List<string> order = [];

    // A patch applied while another records (e.g. from a custom operation) reports to both.
    internal KVPatchChangeRecorder(KVPatchChangeRecorder? outer)
    {
        this.Outer = outer;
    }

    internal KVPatchChangeRecorder? Outer { get; }

    internal void Record(string canonicalPath, object? oldValue, object? newValue, KVChangeKind kind)
    {
        this.Outer?.Record(canonicalPath, oldValue, newValue, kind);
        var path = KVPath.Normalize(canonicalPath);
        var existedBefore = kind switch
        {
            // A nested slot initialized over another type replaces it (OldValue is that type's token).
            KVChangeKind.Added => oldValue is not null,
            KVChangeKind.Value => oldValue is not null,
            _ => true, // Removed, Moved, Replaced
        };
        var existsAfter = kind switch
        {
            KVChangeKind.Removed => false,
            KVChangeKind.Value => newValue is not null,
            _ => true, // Added, Moved, Replaced (a reset or discarded node stays in place)
        };

        // An item or nested node added or removed, or a subtree replaced (RESET, DISCARD): the entry stands
        // for everything beneath it, whatever the patch wrote there before or after.
        var coversSubtree = kind is KVChangeKind.Added or KVChangeKind.Removed or KVChangeKind.Replaced;

        if (this.entries.TryGetValue(path, out var entry))
        {
            // Several changes to one path in a patch: keep its state before the first and after the last.
            // A replacement (RESET, DISCARD) changes the data in place, not whether the path exists.
            if (kind != KVChangeKind.Replaced)
            {
                entry.ExistsAfter = existsAfter;
            }

            entry.NewValue = newValue;
            entry.IsValueOnly &= kind == KVChangeKind.Value;
            entry.CoversSubtree |= coversSubtree;
            return;
        }

        this.entries[path] = new Entry
        {
            ExistedBefore = existedBefore,
            ExistsAfter = existsAfter,
            OldValue = oldValue,
            NewValue = newValue,
            IsValueOnly = kind == KVChangeKind.Value,
            CoversSubtree = coversSubtree,
        };
        this.order.Add(path);
    }

    // The patch's net changes, in the order they first happened. A path whose entry covers its subtree
    // stands for everything beneath it, as in KVRootNode.GetAllChanges: writes under a new item are part of
    // its Added entry, and an item added and removed again leaves nothing. A field written back to its old
    // value is no change.
    internal IReadOnlyList<KVChangeDelta> ToDeltas()
    {
        var deltas = new List<KVChangeDelta>(this.order.Count);
        foreach (var path in this.order)
        {
            var entry = this.entries[path];
            if (this.IsCoveredByAnAncestor(path))
            {
                continue;
            }

            KVChangeDeltaType? type = (entry.ExistedBefore, entry.ExistsAfter) switch
            {
                (false, true) => KVChangeDeltaType.Added,
                (true, false) => KVChangeDeltaType.Removed,
                (true, true) when !(entry.IsValueOnly && KVValues.AreEqual(entry.OldValue, entry.NewValue)) => KVChangeDeltaType.Updated,
                _ => null,
            };
            if (type is null)
            {
                continue;
            }

            deltas.Add(new KVChangeDelta(path, type.Value)
            {
                OldValue = entry.IsValueOnly ? entry.OldValue : null,
                NewValue = entry.IsValueOnly ? entry.NewValue : null,
            });
        }

        return deltas;
    }

    // Walks the path's ancestors (a lookup each), so the cost follows the path's depth, not the number of
    // recorded changes.
    private bool IsCoveredByAnAncestor(string path)
    {
        var lookup = this.entries.GetAlternateLookup<ReadOnlySpan<char>>();
        var ancestor = path.AsSpan();
        while (!ancestor.IsEmpty)
        {
            var slash = ancestor.LastIndexOf('/');
            ancestor = slash < 0 ? [] : ancestor[..slash];
            if (lookup.TryGetValue(ancestor, out var entry) && entry.CoversSubtree)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class Entry
    {
        public bool ExistedBefore { get; init; }

        public bool ExistsAfter { get; set; }

        public object? OldValue { get; init; }

        public object? NewValue { get; set; }

        public bool IsValueOnly { get; set; }

        public bool CoversSubtree { get; set; }
    }
}
