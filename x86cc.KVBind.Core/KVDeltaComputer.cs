using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal static class KVDeltaComputer
{
    internal static KVChangeDeltaGroup Compute(KVNode node, string nodePath, bool isCollectionItem)
    {
        var deltas = new List<KVChangeDelta>();

        // Nested node type change: emit a single slot-level delta instead of individual field deltas.
        if (!isCollectionItem && !string.IsNullOrWhiteSpace(nodePath))
        {
            if (TryCreateNestedNodeTypeDelta(node.Model, nodePath, out var typeDelta))
                return new KVChangeDeltaGroup([typeDelta], []);
        }

        // New (draft-only) collection item.
        if (isCollectionItem && !node.Model.Overlay.IsSnapshotBacked(node.Model.DataPath) && HasDraftState(node))
            return new KVChangeDeltaGroup([new KVChangeDelta(nodePath, KVChangeDeltaType.Added)], []);

        // Field-level changes.
        foreach (var field in node.Definition.Fields)
        {
            var absPath = KVPath.Combine(node.Model.DataPath, field.SubSegmentPath);
            var canonPath = KVPath.Combine(nodePath, field.SubSegmentPath);

            if (node.Model.Overlay.HasRemovedPath(absPath))
            {
                if (node.Model.Overlay.TryGetSnapshotValue(absPath, out _))
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Removed));
            }
            else if (node.Model.Overlay.TryGetDraftValue(absPath, out var draftVal))
            {
                var hasSnap = node.Model.Overlay.TryGetSnapshotValue(absPath, out var snapVal);
                if (!hasSnap)
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Added));
                else if (!Equals(snapVal, draftVal))
                    deltas.Add(new KVChangeDelta(canonPath, KVChangeDeltaType.Updated));
            }
        }

        var children = new List<KVChangeDeltaGroup>();

        // Field group children.
        foreach (var childDef in node.Definition.Nodes)
        {
            var childNode = childDef.GetChildNode(node);
            if (childNode is null) continue;
            var childPath = KVPath.Combine(nodePath, childDef.SubSegmentPath);
            var childDeltas = childNode.ComputeDeltas(childPath, false);
            if (childDeltas.Deltas.Count > 0 || childDeltas.Children.Count > 0)
                children.Add(childDeltas);
        }

        // Collections.
        foreach (var collDef in node.Definition.Collections)
        {
            var collNode = collDef.GetCollection(node);
            if (collNode is not KVCollectionNodeBase collBase) continue;
            var collPath = KVPath.Combine(nodePath, collDef.SubSegmentPath);
            var collDeltas = collBase.ComputeDeltas(collPath);
            if (collDeltas.Deltas.Count > 0 || collDeltas.Children.Count > 0)
                children.Add(collDeltas);
        }

        // Nested nodes.
        foreach (var nestedDef in node.Definition.NestedNodes)
        {
            var nestedPath = KVPath.Combine(nodePath, nestedDef.SubSegmentPath);
            var slotModel = node.GetNestedNodeModel(nestedDef.SubSegmentPath);
            var activeNode = node.GetActiveNestedNode(nestedDef, slotModel);

            if (activeNode is not null)
            {
                var nestedDeltas = activeNode.ComputeDeltas(nestedPath, false);
                if (nestedDeltas.Deltas.Count > 0 || nestedDeltas.Children.Count > 0)
                    children.Add(nestedDeltas);
            }
            else
            {
                var typeAbsPath = KVPath.Combine(slotModel.DataPath, KVNestedNode.TypeKey);
                if (node.Model.Overlay.HasRemovedPath(typeAbsPath) && node.Model.Overlay.TryGetSnapshotValue(typeAbsPath, out _))
                    deltas.Add(new KVChangeDelta(nestedPath, KVChangeDeltaType.Removed));
            }
        }

        return new KVChangeDeltaGroup(deltas, children);
    }

    private static bool HasDraftState(KVNode node)
    {
        if (node.Model.Overlay.HasDraftState(node.Model.DataPath)) return true;
        foreach (var childDef in node.Definition.Nodes)
        {
            var childNode = childDef.GetChildNode(node);
            if (childNode is KVNode kvChild && HasDraftState(kvChild)) return true;
        }
        return false;
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
