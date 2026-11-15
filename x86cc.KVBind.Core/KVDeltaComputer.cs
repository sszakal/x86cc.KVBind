using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal static class KVDeltaComputer
{
    internal static KVChangeDeltaGroup Compute(KVNode node, string nodePath, bool isCollectionItem)
    {
        var deltas = new List<KVChangeDelta>();

        // Nested node type change: emit a single slot-level delta instead of individual field deltas.
        if (!isCollectionItem && !string.IsNullOrWhiteSpace(nodePath) && TryCreateNestedNodeTypeDelta(node.Model, nodePath, out var typeDelta))
        {
            return new KVChangeDeltaGroup([typeDelta], []);
        }

        // New (draft-only) collection item.
        if (isCollectionItem && !node.Model.Overlay.IsSnapshotBacked(node.Model.DataPath) && node.Model.Overlay.HasDraftState(node.Model.DataPath))
            return new KVChangeDeltaGroup([new KVChangeDelta(nodePath, KVChangeDeltaType.Added)], []);

        AddFieldDeltas(node, nodePath, deltas);

        var children = new List<KVChangeDeltaGroup>();
        AddFieldGroupChildren(node, nodePath, children);
        AddCollectionChildren(node, nodePath, children);
        AddNestedNodeChanges(node, nodePath, deltas, children);

        return new KVChangeDeltaGroup(deltas, children);
    }

    // Field-level changes.
    private static void AddFieldDeltas(KVNode node, string nodePath, List<KVChangeDelta> deltas)
    {
        var overlay = node.Model.Overlay;
        foreach (var field in node.Definition.Fields)
        {
            var absPath = KVPath.Combine(node.Model.DataPath, field.SubSegmentPath);
            var canonPath = KVPath.Combine(nodePath, field.SubSegmentPath);

            if (overlay.HasRemovedPath(absPath))
            {
                if (overlay.TryGetSnapshotValue(absPath, out _))
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Removed));
            }
            else if (overlay.TryGetDraftValue(absPath, out var draftVal))
            {
                var hasSnap = overlay.TryGetSnapshotValue(absPath, out var snapVal);
                if (!hasSnap)
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Added));
                else if (!Equals(snapVal, draftVal))
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Updated));
            }
        }
    }

    // Field group children.
    private static void AddFieldGroupChildren(KVNode node, string nodePath, List<KVChangeDeltaGroup> children)
    {
        foreach (var childDef in node.Definition.Nodes)
        {
            var childNode = childDef.GetChildNode(node);
            if (childNode is null) continue;
            var childPath = KVPath.Combine(nodePath, childDef.SubSegmentPath);
            AddIfChanged(children, childNode.ComputeDeltas(childPath, false));
        }
    }

    // Collections.
    private static void AddCollectionChildren(KVNode node, string nodePath, List<KVChangeDeltaGroup> children)
    {
        foreach (var collDef in node.Definition.Collections)
        {
            var collNode = collDef.GetCollection(node);
            if (collNode is not KVCollectionNodeBase collBase) continue;
            var collPath = KVPath.Combine(nodePath, collDef.SubSegmentPath);
            AddIfChanged(children, collBase.ComputeDeltas(collPath));
        }
    }

    // Nested nodes: an active node contributes its own changes; a dropped one is a slot-level removal.
    private static void AddNestedNodeChanges(KVNode node, string nodePath, List<KVChangeDelta> deltas, List<KVChangeDeltaGroup> children)
    {
        foreach (var nestedDef in node.Definition.NestedNodes)
        {
            var nestedPath = KVPath.Combine(nodePath, nestedDef.SubSegmentPath);
            var slotModel = node.GetNestedNodeModel(nestedDef.SubSegmentPath);
            var activeNode = node.GetActiveNestedNode(nestedDef, slotModel);

            if (activeNode is not null)
            {
                AddIfChanged(children, activeNode.ComputeDeltas(nestedPath, false));
            }
            else
            {
                var typeAbsPath = KVPath.Combine(slotModel.DataPath, KVNestedNode.TypeKey);
                if (node.Model.Overlay.HasRemovedPath(typeAbsPath) && node.Model.Overlay.TryGetSnapshotValue(typeAbsPath, out _))
                    deltas.Add(new KVChangeDelta(nestedPath, KVChangeDeltaType.Removed));
            }
        }
    }

    private static void AddIfChanged(List<KVChangeDeltaGroup> children, KVChangeDeltaGroup group)
    {
        if (group.Deltas.Count > 0 || group.Children.Count > 0)
            children.Add(group);
    }

    private static bool TryCreateNestedNodeTypeDelta(KVModel model, string nodePath, out KVChangeDelta delta)
    {
        delta = default!;
        var typePath = KVPath.Combine(nodePath, KVNestedNode.TypeKey);

        if (model.Overlay.HasRemovedPath(typePath))
        {
            if (!model.Overlay.TryGetSnapshotValue(typePath, out _)) return false;
            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Removed);
            return true;
        }

        if (!model.Overlay.TryGetDraftValue(typePath, out var overlayValue)) return false;

        var hasSnapshot = model.Overlay.TryGetSnapshotValue(typePath, out var snapshotValue);
        if (!hasSnapshot)
        {
            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Added);
            return true;
        }

        if (!Equals(snapshotValue, overlayValue))
        {
            delta = new KVChangeDelta(nodePath, KVChangeDeltaType.Updated);
            return true;
        }

        return false;
    }
}
