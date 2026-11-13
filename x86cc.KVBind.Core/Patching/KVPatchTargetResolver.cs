using System;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal static class KVPatchTargetResolver
{
    internal static KVPatchTarget Resolve(KVNode root, KVPatchOperation operation)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(operation);

        var segments = operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (string.Equals(operation.OperationCode, KVPatchOperations.Discard, StringComparison.OrdinalIgnoreCase))
        {
            return new KVPathPatchTarget { CanonicalPath = string.Join('/', segments) };
        }

        // A custom operation addressed to "/" targets the root aggregate itself.
        if (segments.Length == 0 && !KVPatchOperations.IsBuiltIn(operation.OperationCode))
        {
            return new KVNodePatchTarget { Node = root, Definition = root.Definition, CanonicalPath = string.Empty };
        }

        return Resolve(root, operation, segments, depth: 0, currentCanonicalPath: string.Empty);
    }

    private static KVPatchTarget Resolve(KVNode node, KVPatchOperation operation, string[] segments, int depth, string currentCanonicalPath)
    {
        if (segments.Length == depth)
        {
            throw new InvalidOperationException($"Patch operation '{operation.OperationCode}' for '{operation.Path}' does not target a concrete member.");
        }

        var segment = segments[depth];
        var fieldDefinition = node.Definition.FindField(segment);
        if (fieldDefinition is not null)
        {
            return ResolveField(node, operation, fieldDefinition, segments, depth, currentCanonicalPath);
        }

        var nodeDefinition = node.Definition.FindNode(segment);
        if (nodeDefinition is not null)
        {
            return ResolveFieldGroup(node, operation, nodeDefinition, segments, depth, currentCanonicalPath);
        }

        var collectionDefinition = node.Definition.FindCollection(segment);
        if (collectionDefinition is not null)
        {
            return ResolveCollection(node, operation, collectionDefinition, segments, depth, currentCanonicalPath);
        }

        var nestedNodeDefinition = node.Definition.FindNestedNode(segment);
        if (nestedNodeDefinition is not null)
        {
            return ResolveNestedNode(node, operation, nestedNodeDefinition, segments, depth, currentCanonicalPath);
        }

        throw new InvalidOperationException($"Unable to resolve path segment '{segment}' in patch path '{operation.Path}'.");
    }

    private static KVPatchTarget ResolveField(KVNode node, KVPatchOperation operation, KVFieldDefinition fieldDefinition, string[] segments, int depth, string currentCanonicalPath)
    {
        var segment = segments[depth];
        if (segments.Length != depth + 1)
        {
            throw new InvalidOperationException($"Field '{segment}' in patch path '{operation.Path}' must be the final path segment.");
        }

        var canonicalPath = BuildPath(currentCanonicalPath, segment);
        EnsurePatchable(fieldDefinition, operation, canonicalPath);
        return new KVFieldPatchTarget
        {
            Node = node,
            Definition = fieldDefinition,
            FieldKey = segment,
            CanonicalPath = canonicalPath
        };
    }

    private static KVPatchTarget ResolveFieldGroup(KVNode node, KVPatchOperation operation, KVNodeDefinition nodeDefinition, string[] segments, int depth, string currentCanonicalPath)
    {
        var childNode = nodeDefinition.GetChildNode(node)
                        ?? throw new InvalidOperationException($"Node segment '{segments[depth]}' resolved to null.");
        var groupPath = BuildPath(currentCanonicalPath, nodeDefinition.SubSegmentPath);
        EnsurePatchable(nodeDefinition, operation, groupPath);
        if (segments.Length == depth + 1 && TargetsNodeItself(operation))
        {
            return new KVNodePatchTarget
            {
                Node = childNode,
                Definition = nodeDefinition,
                CanonicalPath = groupPath
            };
        }

        return Resolve(childNode, operation, segments, depth + 1, groupPath);
    }

    // RESET and custom operations address the node; every other built-in addresses a member beneath it.
    private static bool TargetsNodeItself(KVPatchOperation operation) =>
        string.Equals(operation.OperationCode, KVPatchOperations.Reset, StringComparison.OrdinalIgnoreCase)
        || !KVPatchOperations.IsBuiltIn(operation.OperationCode);

    private static KVPatchTarget ResolveCollection(KVNode owner, KVPatchOperation operation, KVCollectionDefinition collectionDefinition, string[] segments, int depth, string currentCanonicalPath)
    {
        var collectionNode = collectionDefinition.GetCollection(owner)
                             ?? throw new InvalidOperationException($"Collection '{collectionDefinition.SubSegmentPath}' resolved to null.");
        var collectionPath = BuildPath(currentCanonicalPath, collectionDefinition.SubSegmentPath);
        EnsurePatchable(collectionDefinition, operation, collectionPath);

        if (segments.Length == depth + 1)
        {
            return new KVCollectionPatchTarget
            {
                Owner = owner,
                Collection = collectionNode,
                Definition = collectionDefinition,
                CanonicalPath = collectionPath
            };
        }

        var itemId = segments[depth + 1];
        var itemNode = collectionNode.GetById(itemId)
                       ?? throw new InvalidOperationException($"Collection child '{itemId}' not found for path '{operation.Path}'.");

        if (segments.Length == depth + 2)
        {
            return new KVCollectionItemPatchTarget
            {
                Owner = owner,
                Collection = collectionNode,
                Definition = collectionDefinition,
                Item = itemNode,
                ItemId = itemId,
                CanonicalPath = BuildPath(collectionPath, itemId)
            };
        }

        return Resolve(itemNode, operation, segments, depth + 2, BuildPath(collectionPath, itemId));
    }

    private static KVPatchTarget ResolveNestedNode(KVNode owner, KVPatchOperation operation, KVNestedNodeDefinition nestedNodeDefinition, string[] segments, int depth, string currentCanonicalPath)
    {
        var nestedPath = BuildPath(currentCanonicalPath, nestedNodeDefinition.SubSegmentPath);
        EnsurePatchable(nestedNodeDefinition, operation, nestedPath);
        var nestedModel = owner.GetNestedNodeModel(nestedNodeDefinition.SubSegmentPath);
        if (segments.Length == depth + 1)
        {
            return new KVNestedNodePatchTarget
            {
                Owner = owner,
                Definition = nestedNodeDefinition,
                SlotModel = nestedModel,
                CanonicalPath = nestedPath
            };
        }

        var activeNode = owner.GetActiveNestedNode(nestedNodeDefinition, nestedModel)
                         ?? throw new InvalidOperationException($"Nested node '{nestedPath}' is not initialized for path '{operation.Path}'.");
        return Resolve(activeNode, operation, segments, depth + 1, nestedPath);
    }

    // A PatchReadOnly() member rejects every built-in operation that targets it or anything beneath it.
    // Custom operations are application code registered on the node, so they pass.
    private static void EnsurePatchable(KVDefinition definition, KVPatchOperation operation, string memberPath)
    {
        if (definition.IsPatchReadOnly && KVPatchOperations.IsBuiltIn(operation.OperationCode))
            throw KVPatchRuntime.PatchReadOnly(operation, memberPath);
    }

    private static string BuildPath(string prefix, string segment)
    {
        return KVPath.Combine(prefix, segment);
    }
}
