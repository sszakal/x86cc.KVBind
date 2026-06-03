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

        return Resolve(root, operation, segments, depth: 0, currentCanonicalPath: string.Empty);
    }

    private static KVPatchTarget Resolve(KVNode node, KVPatchOperation operation, string[] segments, int depth, string currentCanonicalPath)
    {
        if (segments.Length == depth)
        {
            throw new InvalidOperationException($"Patch operation '{operation.OperationCode}' for '{operation.Path}' does not target a concrete member.");
        }

        var segment = segments[depth];
        var fieldDefinition = node.Definition.Fields.Find(field => string.Equals(field.SubSegmentPath, segment, StringComparison.Ordinal));
        if (fieldDefinition is not null)
        {
            if (segments.Length != depth + 1)
            {
                throw new InvalidOperationException($"Field '{segment}' in patch path '{operation.Path}' must be the final path segment.");
            }

            var canonicalPath = BuildPath(currentCanonicalPath, segment);
            return new KVFieldPatchTarget
            {
                Node = node,
                Definition = fieldDefinition,
                FieldKey = segment,
                CanonicalPath = canonicalPath
            };
        }

        var nodeDefinition = node.Definition.Nodes.Find(definition => string.Equals(definition.SubSegmentPath, segment, StringComparison.Ordinal));
        if (nodeDefinition is not null)
        {
            var childNode = nodeDefinition.GetChildNode(node)
                            ?? throw new InvalidOperationException($"Node segment '{segment}' resolved to null.");
            return Resolve(childNode, operation, segments, depth + 1, BuildPath(currentCanonicalPath, nodeDefinition.SubSegmentPath));
        }

        var collectionDefinition = node.Definition.Collections.Find(definition => string.Equals(definition.SubSegmentPath, segment, StringComparison.Ordinal));
        if (collectionDefinition is not null)
        {
            return ResolveCollection(node, operation, collectionDefinition, segments, depth, currentCanonicalPath);
        }

        var nestedNodeDefinition = node.Definition.NestedNodes.Find(definition => string.Equals(definition.SubSegmentPath, segment, StringComparison.Ordinal));
        if (nestedNodeDefinition is not null)
        {
            return ResolveNestedNode(node, operation, nestedNodeDefinition, segments, depth, currentCanonicalPath);
        }

        throw new InvalidOperationException($"Unable to resolve path segment '{segment}' in patch path '{operation.Path}'.");
    }

    private static KVPatchTarget ResolveCollection(KVNode owner, KVPatchOperation operation, KVCollectionDefinition collectionDefinition, string[] segments, int depth, string currentCanonicalPath)
    {
        var collectionNode = collectionDefinition.GetCollection(owner)
                             ?? throw new InvalidOperationException($"Collection '{collectionDefinition.SubSegmentPath}' resolved to null.");
        var collectionPath = BuildPath(currentCanonicalPath, collectionDefinition.SubSegmentPath);

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

    private static string BuildPath(string prefix, string segment)
    {
        return KVPath.Combine(prefix, segment);
    }
}
