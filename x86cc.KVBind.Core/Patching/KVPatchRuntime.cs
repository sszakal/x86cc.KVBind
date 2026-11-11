using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

[Flags]
internal enum KVPatchTargetKind
{
    Path = 1,
    Field = 2,
    Collection = 4,
    CollectionItem = 8,
    NestedNode = 16,
    Node = 32
}

internal static class KVPatchRuntime
{
    internal static void Apply(KVRootNode root, IEnumerable<KVPatchOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(operations);

        var registry = KVPatchOperationRegistry.CreateDefault();
        var context = new KVPatchExecutionContext
        {
            Root = root
        };

        foreach (var operation in operations)
        {
            ArgumentNullException.ThrowIfNull(operation);
            var target = KVPatchTargetResolver.Resolve(root, operation);
            registry.Apply(target, operation, context);
        }
    }

    internal static InvalidOperationException PatchReadOnly(KVPatchOperation operation, string memberPath)
    {
        return new InvalidOperationException($"Patch operation '{operation.OperationCode}' for '{operation.Path}' is not allowed: '{memberPath}' is patch read-only.");
    }

    // True when the definition declares a PatchReadOnly() member anywhere beneath it.
    internal static bool ContainsPatchReadOnly(KVNodeDefinition definition)
    {
        foreach (var field in definition.Fields)
            if (field.IsPatchReadOnly) return true;

        foreach (var node in definition.Nodes)
            if (node.IsPatchReadOnly || ContainsPatchReadOnly(node)) return true;

        foreach (var collection in definition.Collections)
        {
            if (collection.IsPatchReadOnly) return true;
            foreach (var item in collection.ItemDefinitionsByToken.Values)
                if (ContainsPatchReadOnly(item.NodeDefinition)) return true;
        }

        foreach (var nested in definition.NestedNodes)
        {
            if (nested.IsPatchReadOnly) return true;
            foreach (var type in nested.TypeDefinitionsByToken.Values)
                if (ContainsPatchReadOnly(type.NodeDefinition)) return true;
        }

        return false;
    }
}

internal abstract class KVPatchTarget
{
    public required string CanonicalPath { get; init; }
}

internal sealed class KVPathPatchTarget : KVPatchTarget;

internal sealed class KVFieldPatchTarget : KVPatchTarget
{
    public required KVNode Node { get; init; }

    public required KVFieldDefinition Definition { get; init; }

    public required string FieldKey { get; init; }
}

internal sealed class KVCollectionPatchTarget : KVPatchTarget
{
    public required KVNode Owner { get; init; }

    public required IKVCollectionNode Collection { get; init; }

    public required KVCollectionDefinition Definition { get; init; }
}

internal sealed class KVCollectionItemPatchTarget : KVPatchTarget
{
    public required KVNode Owner { get; init; }

    public required IKVCollectionNode Collection { get; init; }

    public required KVCollectionDefinition Definition { get; init; }

    public required KVNode Item { get; init; }

    public required string ItemId { get; init; }
}

// A field group (KVFieldGroupNode) addressed as a whole — currently only by RESET.
internal sealed class KVNodePatchTarget : KVPatchTarget
{
    public required KVNode Node { get; init; }

    public required KVNodeDefinition Definition { get; init; }
}

internal sealed class KVNestedNodePatchTarget : KVPatchTarget
{
    public required KVNode Owner { get; init; }

    public required KVNestedNodeDefinition Definition { get; init; }

    public required KVModel SlotModel { get; init; }
}

internal sealed class KVPatchExecutionContext
{
    public required KVRootNode Root { get; init; }
}

internal sealed class KVPatchOperationRegistry
{
    private static readonly IReadOnlyDictionary<string, KVPatchOperationDescriptor> BuiltInOperations =
        new Dictionary<string, KVPatchOperationDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            [KVPatchOperations.Set] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Set, KVPatchTargetKind.Field, ApplySet),
            [KVPatchOperations.Unset] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Unset, KVPatchTargetKind.Field, ApplyUnset),
            [KVPatchOperations.Add] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Add, KVPatchTargetKind.Collection, ApplyAdd),
            [KVPatchOperations.Remove] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Remove, KVPatchTargetKind.CollectionItem, ApplyRemove),
            [KVPatchOperations.Move] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Move, KVPatchTargetKind.CollectionItem, ApplyMove),
            [KVPatchOperations.Init] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Init, KVPatchTargetKind.NestedNode, ApplyInit),
            [KVPatchOperations.Drop] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Drop, KVPatchTargetKind.NestedNode, ApplyDrop),
            [KVPatchOperations.Discard] = KVPatchOperationDescriptor.BuiltIn(KVPatchOperations.Discard, KVPatchTargetKind.Path, ApplyDiscard),
            [KVPatchOperations.Reset] = KVPatchOperationDescriptor.BuiltIn(
                KVPatchOperations.Reset,
                KVPatchTargetKind.CollectionItem | KVPatchTargetKind.NestedNode | KVPatchTargetKind.Node,
                ApplyReset)
        };

    public static KVPatchOperationRegistry CreateDefault() => new();

    internal static void EnsureCustomOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        if (KVPatchOperations.IsBuiltIn(operation))
        {
            throw new InvalidOperationException($"Patch operation '{operation}' is built in and cannot be overridden.");
        }
    }

    internal void Apply(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var descriptor = Resolve(target, operation);
        var targetKind = GetTargetKind(target);
        if ((descriptor.TargetKind & targetKind) == 0)
        {
            throw Unsupported(target, operation);
        }

        EnsureCollectionOperationAllowed(target, operation, context);
        descriptor.Invoke(target, operation, context);
    }

    private static void EnsureCollectionOperationAllowed(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var collectionDefinition = target switch
        {
            KVCollectionPatchTarget collectionTarget => collectionTarget.Definition,

            // AllowOperations governs structural operations; a custom operation sent to an item is the
            // item's own behavior and is not subject to it.
            KVCollectionItemPatchTarget itemTarget when KVPatchOperations.IsBuiltIn(operation.OperationCode) => itemTarget.Definition,
            _ => null
        };

        if (collectionDefinition is not null)
        {
            if (!collectionDefinition.IsOperationAllowed(operation.OperationCode))
                throw Disallowed(operation.OperationCode, collectionDefinition, operation);
            return;
        }

        if (target is KVPathPatchTarget)
        {
            EnsureDiscardKeepsRestrictedMembership(context.Root, target.CanonicalPath, operation);
            EnsureDiscardKeepsPatchReadOnly(context.Root, target.CanonicalPath, operation);
        }
    }

    // DISCARD reverts draft changes; reject it when that would revert a change made (by application code)
    // to a PatchReadOnly() member.
    private static void EnsureDiscardKeepsPatchReadOnly(KVNode root, string discardPath, KVPatchOperation operation)
    {
        var overlay = root.Model.Overlay;
        foreach (var memberPath in EnumeratePatchReadOnlyPaths(root, discardPath))
        {
            string affectedPath;
            if (discardPath.Length == 0 || KVPath.IsSameOrDescendant(memberPath, discardPath))
                affectedPath = memberPath;
            else if (KVPath.IsSameOrDescendant(discardPath, memberPath))
                affectedPath = discardPath;
            else
                continue;

            if (overlay.HasDraftState(affectedPath))
                throw KVPatchRuntime.PatchReadOnly(operation, memberPath);
        }
    }

    // Data paths of the PatchReadOnly() members of the bound graph. Objects born in the draft that lie
    // wholly under the discard path vanish with it, so their members are skipped.
    private static IEnumerable<string> EnumeratePatchReadOnlyPaths(KVNode node, string discardPath)
    {
        foreach (var fieldDefinition in node.Definition.Fields)
        {
            if (fieldDefinition.IsPatchReadOnly)
                yield return KVPath.Combine(node.Model.DataPath, fieldDefinition.SubSegmentPath);
        }

        foreach (var nodeDefinition in node.Definition.Nodes)
        {
            var child = nodeDefinition.GetChildNode(node);
            if (nodeDefinition.IsPatchReadOnly)
            {
                yield return child.Model.DataPath;
                continue;
            }

            foreach (var path in EnumeratePatchReadOnlyPaths(child, discardPath))
                yield return path;
        }

        foreach (var collectionDefinition in node.Definition.Collections)
        {
            if (collectionDefinition.GetCollection(node) is not KVCollectionNodeBase collection) continue;
            if (collectionDefinition.IsPatchReadOnly)
            {
                yield return collection.Model.DataPath;
                continue;
            }

            foreach (var itemId in collection.GetActiveItemIds())
            {
                var itemPath = KVPath.Combine(collection.Model.DataPath, itemId);
                if (IsBornInDraftUnder(node.Model.Overlay, itemPath, discardPath)) continue;
                if (((IKVCollectionNode)collection).GetById(itemId) is not { } item) continue;

                foreach (var path in EnumeratePatchReadOnlyPaths(item, discardPath))
                    yield return path;
            }
        }

        foreach (var nestedDefinition in node.Definition.NestedNodes)
        {
            var nestedModel = node.GetNestedNodeModel(nestedDefinition.SubSegmentPath);
            if (nestedDefinition.IsPatchReadOnly)
            {
                yield return nestedModel.DataPath;
                continue;
            }

            if (node.GetActiveNestedNode(nestedDefinition, nestedModel) is not { } nestedNode) continue;
            if (IsBornInDraftUnder(node.Model.Overlay, nestedModel.DataPath, discardPath)) continue;

            foreach (var path in EnumeratePatchReadOnlyPaths(nestedNode, discardPath))
                yield return path;
        }
    }

    // A collection item or nested node created in the draft carries its $type in the draft.
    private static bool IsBornInDraftUnder(KVOverlay overlay, string objectPath, string discardPath)
    {
        if (discardPath.Length > 0 && !KVPath.IsSameOrDescendant(objectPath, discardPath)) return false;
        return overlay.TryGetDraftValue(KVPath.Combine(objectPath, KVNestedNode.TypeKey), out _);
    }

    // DISCARD reverts a restricted collection's $items to the snapshot, which can add, remove or reorder
    // items. Require the structural operation that the revert would amount to.
    private static void EnsureDiscardKeepsRestrictedMembership(KVNode root, string discardPath, KVPatchOperation operation)
    {
        foreach (var collection in EnumerateCollections(root, discardPath))
        {
            var definition = collection.Definition;
            if (definition.AllowedOperations is null) continue;

            var itemsPath = KVPath.Combine(collection.Model.DataPath, "$items");
            if (discardPath.Length > 0 && !KVPath.IsSameOrDescendant(itemsPath, discardPath)) continue;

            var current = collection.GetActiveItemIds();
            var snapshot = collection.Model.Overlay.TryGetSnapshotValue(itemsPath, out var snapshotValue)
                ? KVMerge.ExtractItemIds(snapshotValue)
                : [];

            var currentSet = new HashSet<string>(current, StringComparer.Ordinal);
            var snapshotSet = new HashSet<string>(snapshot, StringComparer.Ordinal);

            if (!snapshotSet.IsSubsetOf(currentSet) && !definition.IsOperationAllowed(KVPatchOperations.Add))
                throw Disallowed(KVPatchOperations.Add, definition, operation);
            if (!currentSet.IsSubsetOf(snapshotSet) && !definition.IsOperationAllowed(KVPatchOperations.Remove))
                throw Disallowed(KVPatchOperations.Remove, definition, operation);

            var currentOrder = current.Where(snapshotSet.Contains);
            var snapshotOrder = snapshot.Where(currentSet.Contains);
            if (!currentOrder.SequenceEqual(snapshotOrder, StringComparer.Ordinal) && !definition.IsOperationAllowed(KVPatchOperations.Move))
                throw Disallowed(KVPatchOperations.Move, definition, operation);
        }
    }

    // Bound collections whose subtree a discard of `discardPath` may touch. Items that exist only in the
    // draft and lie wholly under the discard path vanish with it, so their nested collections are skipped.
    private static IEnumerable<KVCollectionNodeBase> EnumerateCollections(KVNode node, string discardPath)
    {
        foreach (var nodeDefinition in node.Definition.Nodes)
        {
            foreach (var collection in EnumerateCollections(nodeDefinition.GetChildNode(node), discardPath))
                yield return collection;
        }

        foreach (var collectionDefinition in node.Definition.Collections)
        {
            if (collectionDefinition.GetCollection(node) is not KVCollectionNodeBase collection) continue;
            yield return collection;

            var itemsPath = KVPath.Combine(collection.Model.DataPath, "$items");
            var snapshotIds = collection.Model.Overlay.TryGetSnapshotValue(itemsPath, out var snapshotValue)
                ? new HashSet<string>(KVMerge.ExtractItemIds(snapshotValue), StringComparer.Ordinal)
                : [];

            foreach (var itemId in collection.GetActiveItemIds())
            {
                var itemPath = KVPath.Combine(collection.Model.DataPath, itemId);
                if (!snapshotIds.Contains(itemId) && (discardPath.Length == 0 || KVPath.IsSameOrDescendant(itemPath, discardPath))) continue;
                if (((IKVCollectionNode)collection).GetById(itemId) is not { } item) continue;

                foreach (var nested in EnumerateCollections(item, discardPath))
                    yield return nested;
            }
        }

        foreach (var nestedDefinition in node.Definition.NestedNodes)
        {
            var nestedModel = node.GetNestedNodeModel(nestedDefinition.SubSegmentPath);
            if (node.GetActiveNestedNode(nestedDefinition, nestedModel) is not { } nestedNode) continue;

            foreach (var collection in EnumerateCollections(nestedNode, discardPath))
                yield return collection;
        }
    }

    private static InvalidOperationException Disallowed(string disallowed, KVCollectionDefinition definition, KVPatchOperation operation)
    {
        return new InvalidOperationException($"Patch operation '{operation.OperationCode}' for '{operation.Path}' is not allowed: collection '{definition.SubSegmentPath}' does not allow '{disallowed}'.");
    }

    private static KVPatchOperationDescriptor Resolve(KVPatchTarget target, KVPatchOperation operation)
    {
        if (BuiltInOperations.TryGetValue(operation.OperationCode, out var builtInOperation))
        {
            return builtInOperation;
        }

        if (target is KVCollectionPatchTarget collectionTarget
            && collectionTarget.Definition.PatchOperations.TryGetValue(operation.OperationCode, out var customOperation))
        {
            return customOperation;
        }

        if (TryGetTargetNode(target) is { } node
            && node.Definition.PatchOperations.TryGetValue(operation.OperationCode, out var nodeOperation))
        {
            return nodeOperation;
        }

        throw Unsupported(target, operation);
    }

    // The node a node-level custom operation runs on: the root or a field group, a collection item, or
    // the active subtype of a nested node slot (null when the slot is not initialized).
    internal static KVNode? TryGetTargetNode(KVPatchTarget target)
    {
        return target switch
        {
            KVNodePatchTarget nodeTarget => nodeTarget.Node,
            KVCollectionItemPatchTarget itemTarget => itemTarget.Item,
            KVNestedNodePatchTarget nestedTarget => nestedTarget.Owner.GetActiveNestedNode(nestedTarget.Definition, nestedTarget.SlotModel),
            _ => null
        };
    }

    private static KVPatchTargetKind GetTargetKind(KVPatchTarget target)
    {
        return target switch
        {
            KVPathPatchTarget => KVPatchTargetKind.Path,
            KVFieldPatchTarget => KVPatchTargetKind.Field,
            KVCollectionPatchTarget => KVPatchTargetKind.Collection,
            KVCollectionItemPatchTarget => KVPatchTargetKind.CollectionItem,
            KVNestedNodePatchTarget => KVPatchTargetKind.NestedNode,
            KVNodePatchTarget => KVPatchTargetKind.Node,
            _ => throw new InvalidOperationException($"Unknown patch target type '{target.GetType().FullName}'.")
        };
    }

    private static InvalidOperationException Unsupported(KVPatchTarget target, KVPatchOperation operation)
    {
        return new InvalidOperationException($"Patch operation '{operation.OperationCode}' is not valid for path '{operation.Path}' resolved to '{target.CanonicalPath}'.");
    }

    private static void ApplySet(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var fieldTarget = (KVFieldPatchTarget)target;
        var value = operation.Value;
        if (fieldTarget.Definition.AllowedValues is not null && value is not null)
        {
            if (value is string token)
            {
                try
                {
                    value = fieldTarget.Definition.AllowedValues.DenormalizeFromStorage(token);
                }
                catch (InvalidOperationException)
                {
                    // Keep unknown token as-is so validation can report allowed_values.
                }
            }
            else if (!fieldTarget.Definition.AllowedValues.IsAllowed(value))
            {
                throw new InvalidOperationException($"Value for field '{fieldTarget.CanonicalPath}' is not part of configured allowed values.");
            }
        }
        else if (value is not null)
        {
            value = KVPatchValueConverter.ConvertToFieldType(value, fieldTarget.Definition.ValueType, fieldTarget.CanonicalPath);
        }

        fieldTarget.Node.SetFieldForPatch(fieldTarget.FieldKey, value);
    }

    private static void ApplyUnset(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var fieldTarget = (KVFieldPatchTarget)target;
        fieldTarget.Node.RemoveFieldForPatch(fieldTarget.FieldKey);
    }

    private static void ApplyAdd(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var collectionTarget = (KVCollectionPatchTarget)target;
        var payload = operation.Value as KVAddPatchPayload
                      ?? throw new InvalidOperationException($"ADD patch for '{operation.Path}' requires a KVAddPatchPayload value.");
        collectionTarget.Collection.Create(payload.ItemId, payload.TypeToken);
    }

    private static void ApplyRemove(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var itemTarget = (KVCollectionItemPatchTarget)target;
        itemTarget.Collection.RemoveById(itemTarget.ItemId);
    }

    private static void ApplyMove(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var itemTarget = (KVCollectionItemPatchTarget)target;
        var payload = operation.Value switch
        {
            int toIndex => new KVMovePatchPayload(toIndex),
            KVMovePatchPayload movePayload => movePayload,
            _ => null
        };
        if (payload is null)
        {
            throw new InvalidOperationException($"MOVE patch for '{operation.Path}' requires an integer target index or KVMovePatchPayload value.");
        }

        if (!itemTarget.Collection.MoveById(itemTarget.ItemId, payload.ToIndex))
        {
            throw new InvalidOperationException($"Collection path '{operation.Path}' does not contain child '{itemTarget.ItemId}'.");
        }
    }

    private static void ApplyInit(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var nestedTarget = (KVNestedNodePatchTarget)target;
        if (operation.Value is not string typeToken || string.IsNullOrWhiteSpace(typeToken))
        {
            throw new InvalidOperationException($"INIT patch for '{operation.Path}' requires a nested node type token value.");
        }

        nestedTarget.Definition.GetTypeDefinition(typeToken);
        var oldTypeToken = KVNestedNode.GetItemType(nestedTarget.SlotModel);
        nestedTarget.Owner.ClearNestedNodeModelForPatch(nestedTarget.SlotModel);
        KVNestedNode.SetItemType(nestedTarget.SlotModel, typeToken);
        nestedTarget.Owner.DetachNestedNodeForPatch(nestedTarget.Definition.SubSegmentPath);
        nestedTarget.Owner.RebindCurrentContextForPatch();

        // The slot was cleared above, so this is a newly born node: apply its declared defaults.
        var initialized = nestedTarget.Owner.GetActiveNestedNode(nestedTarget.Definition, nestedTarget.SlotModel);
        initialized?.ApplyDefaultsRecursive();
        nestedTarget.Owner.EmitNestedNodeChange(nestedTarget.Definition.SubSegmentPath, oldTypeToken, initialized);
    }

    private static void ApplyDrop(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        var nestedTarget = (KVNestedNodePatchTarget)target;
        var oldTypeToken = KVNestedNode.GetItemType(nestedTarget.SlotModel);
        nestedTarget.Owner.ClearNestedNodeModelForPatch(nestedTarget.SlotModel);
        KVNestedNode.ClearItemType(nestedTarget.SlotModel);
        nestedTarget.Owner.DetachNestedNodeForPatch(nestedTarget.Definition.SubSegmentPath);
        nestedTarget.Owner.RebindCurrentContextForPatch();
        nestedTarget.Owner.EmitNestedNodeChange(nestedTarget.Definition.SubSegmentPath, oldTypeToken, newNode: null);
    }

    private static void ApplyReset(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        // A reset rewrites every value under the node, so it may not contain PatchReadOnly() members.
        void EnsureNoPatchReadOnly(KVNode node)
        {
            if (KVPatchRuntime.ContainsPatchReadOnly(node.Definition))
                throw new InvalidOperationException($"Patch operation '{operation.OperationCode}' for '{operation.Path}' is not allowed: '{target.CanonicalPath}' contains patch read-only members.");
        }

        switch (target)
        {
            case KVCollectionItemPatchTarget itemTarget:
                EnsureNoPatchReadOnly(itemTarget.Item);
                itemTarget.Item.Reset();
                break;
            case KVNestedNodePatchTarget nestedTarget:
                var nested = nestedTarget.Owner.GetActiveNestedNode(nestedTarget.Definition, nestedTarget.SlotModel)
                             ?? throw new InvalidOperationException($"Nested node '{nestedTarget.CanonicalPath}' is not initialized and cannot be reset.");
                EnsureNoPatchReadOnly(nested);
                nested.Reset();
                break;
            case KVNodePatchTarget nodeTarget:
                if (nodeTarget.Definition.IsResettable != true)
                {
                    throw new InvalidOperationException($"Field group '{nodeTarget.CanonicalPath}' is not resettable; declare it with Resettable().");
                }

                EnsureNoPatchReadOnly(nodeTarget.Node);
                nodeTarget.Node.Reset();
                break;
        }
    }

    private static void ApplyDiscard(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(target.CanonicalPath))
        {
            context.Root.Clear();
            return;
        }

        context.Root.Discard(target.CanonicalPath);
    }
}

internal sealed class KVPatchOperationDescriptor
{
    // Payloads usually arrive from web clients: camelCase names, and numbers that may be sent as strings.
    private static readonly JsonSerializerOptions ArgumentJsonOptions = new(JsonSerializerDefaults.Web);

    private const KVPatchTargetKind NodeTargetKinds = KVPatchTargetKind.Node | KVPatchTargetKind.CollectionItem | KVPatchTargetKind.NestedNode;

    private readonly Action<KVPatchTarget, KVPatchOperation, KVPatchExecutionContext> invoke;

    private KVPatchOperationDescriptor(
        string operation,
        KVPatchTargetKind targetKind,
        Type argumentType,
        Action<KVPatchTarget, KVPatchOperation, KVPatchExecutionContext> invoke)
    {
        this.Operation = operation;
        this.TargetKind = targetKind;
        this.ArgumentType = argumentType;
        this.invoke = invoke;
    }

    public string Operation { get; }

    public KVPatchTargetKind TargetKind { get; }

    public Type ArgumentType { get; }

    internal static KVPatchOperationDescriptor BuiltIn(
        string operation,
        KVPatchTargetKind targetKind,
        Action<KVPatchTarget, KVPatchOperation, KVPatchExecutionContext> invoke)
    {
        return new KVPatchOperationDescriptor(operation, targetKind, typeof(KVPatchOperation), invoke);
    }

    internal static KVPatchOperationDescriptor CustomCollection<TParent, TArgument>(string operation, Expression<Func<TParent, Action<TArgument>>> methodSelector)
        where TParent : KVNode
    {
        KVPatchOperationRegistry.EnsureCustomOperation(operation);
        ArgumentNullException.ThrowIfNull(methodSelector);

        var method = GetMethod(methodSelector);
        if (method.ReturnType != typeof(void))
        {
            throw new InvalidOperationException($"Patch operation method '{method.Name}' must return void.");
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(TArgument))
        {
            throw new InvalidOperationException($"Patch operation method '{method.Name}' must accept exactly one '{typeof(TArgument).FullName}' argument.");
        }

        var selector = methodSelector.Compile();
        return new KVPatchOperationDescriptor(
            operation.ToUpperInvariant(),
            KVPatchTargetKind.Collection,
            typeof(TArgument),
            (target, patchOperation, context) =>
            {
                var collectionTarget = (KVCollectionPatchTarget)target;
                var argument = CoerceArgument(patchOperation.Value, typeof(TArgument), patchOperation.OperationCode, patchOperation.Path);
                selector((TParent)collectionTarget.Owner)((TArgument)argument!);
            });
    }

    // A custom operation declared on a node (KVBindBuilder.Operation): the patch targets the node itself.
    internal static KVPatchOperationDescriptor CustomNode<TNode, TArgument>(string operation, Expression<Func<TNode, Action<TArgument>>> methodSelector)
        where TNode : KVNode
    {
        KVPatchOperationRegistry.EnsureCustomOperation(operation);
        ArgumentNullException.ThrowIfNull(methodSelector);
        _ = GetMethod(methodSelector);

        var selector = methodSelector.Compile();
        return new KVPatchOperationDescriptor(
            operation.ToUpperInvariant(),
            NodeTargetKinds,
            typeof(TArgument),
            (target, patchOperation, context) =>
            {
                var argument = CoerceArgument(patchOperation.Value, typeof(TArgument), patchOperation.OperationCode, patchOperation.Path);
                selector((TNode)KVPatchOperationRegistry.TryGetTargetNode(target)!)((TArgument)argument!);
            });
    }

    internal static KVPatchOperationDescriptor CustomNode<TNode>(string operation, Expression<Func<TNode, Action>> methodSelector)
        where TNode : KVNode
    {
        KVPatchOperationRegistry.EnsureCustomOperation(operation);
        ArgumentNullException.ThrowIfNull(methodSelector);
        _ = GetMethod(methodSelector);

        var selector = methodSelector.Compile();
        return new KVPatchOperationDescriptor(
            operation.ToUpperInvariant(),
            NodeTargetKinds,
            typeof(void),
            (target, patchOperation, context) =>
            {
                if (patchOperation.Value is not null)
                {
                    throw new InvalidOperationException($"Patch operation '{patchOperation.OperationCode}' for '{patchOperation.Path}' does not accept a value.");
                }

                selector((TNode)KVPatchOperationRegistry.TryGetTargetNode(target)!)();
            });
    }

    internal static string GetMethodName(LambdaExpression methodSelector)
    {
        ArgumentNullException.ThrowIfNull(methodSelector);
        return GetMethod(methodSelector).Name;
    }

    internal void Invoke(KVPatchTarget target, KVPatchOperation operation, KVPatchExecutionContext context)
    {
        this.invoke(target, operation, context);
    }

    private static MethodInfo GetMethod(LambdaExpression methodSelector)
    {
        Expression body = methodSelector.Body;
        if (body is UnaryExpression unaryExpression && unaryExpression.NodeType == ExpressionType.Convert)
        {
            body = unaryExpression.Operand;
        }

        if (body is MethodCallExpression methodCall
            && methodCall.Object is ConstantExpression { Value: MethodInfo methodInfo })
        {
            return methodInfo;
        }

        throw new InvalidOperationException("Patch operation registration must select an instance method group, for example x => x.GroupItems.");
    }

    private static object? CoerceArgument(object? value, Type argumentType, string operation, string path)
    {
        if (value is null)
        {
            if (!argumentType.IsValueType || Nullable.GetUnderlyingType(argumentType) is not null)
            {
                return null;
            }

            throw new InvalidOperationException($"Patch operation '{operation}' for '{path}' requires a '{argumentType.FullName}' payload.");
        }

        if (argumentType.IsInstanceOfType(value))
        {
            return value;
        }

        try
        {
            return value is JsonElement jsonElement
                ? jsonElement.Deserialize(argumentType, ArgumentJsonOptions)
                : JsonSerializer.Deserialize(JsonSerializer.Serialize(value, ArgumentJsonOptions), argumentType, ArgumentJsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Patch operation '{operation}' for '{path}' payload cannot be converted to '{argumentType.FullName}'.", exception);
        }
    }
}
