using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal readonly record struct KVReactionFrame(
    string NodeCanonicalPath,
    string ChangedPath,
    KVChangeReactionDescriptor Reaction);

internal static class KVChangeReactionRuntime
{
    private const int MaxReactionChainLength = 16;

    internal static void Emit(KVNode source, string canonicalPath, object? oldValue, object? newValue, KVChangeKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(canonicalPath);

        var root = ResolveRoot(source);
        var state = root.ReactionExecutionState;
        var isTopLevel = state.EnterScope();

        try
        {
            // Walk ancestors manually (no iterator allocation) and only compute the relative path
            // (a substring) for ancestors that actually declare change reactions.
            KVNode? ancestor = source;
            while (ancestor is not null)
            {
                if (ancestor.Definition.ChangeReactions.Count > 0)
                {
                    var ancestorPath = ancestor.GetCanonicalPath();
                    var relativePath = KVPath.RelativeTo(canonicalPath, ancestorPath);
                    if (relativePath is not null)
                    {
                        foreach (var reaction in ancestor.Definition.ChangeReactions)
                        {
                            if (reaction.Pattern.Matches(relativePath))
                            {
                                InvokeReaction(state, ancestor, ancestorPath, reaction, relativePath, oldValue, newValue, kind);
                            }
                        }
                    }
                }

                ancestor = ParentNode(ancestor);
            }
        }
        finally
        {
            state.ExitScope(isTopLevel);
        }
    }

    // The data at `replacedPath` changed wholesale without per-value notifications. Runs, once each, every
    // reaction that watches that path or anything beneath it — innermost nodes first, so a derived value
    // on an item is fresh before a total over the items is recomputed.
    internal static void EmitReplaced(KVNode source, string replacedPath)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(replacedPath);

        var root = ResolveRoot(source);
        var state = root.ReactionExecutionState;
        var isTopLevel = state.EnterScope();

        try
        {
            NotifyReplaced(state, root, KVPath.Normalize(replacedPath));
        }
        finally
        {
            state.ExitScope(isTopLevel);
        }
    }

    private static void NotifyReplaced(KVReactionExecutionState state, KVNode node, string replacedPath)
    {
        var nodePath = node.GetCanonicalPath();
        var relativePath = KVPath.RelativeTo(replacedPath, nodePath);

        // Neither inside the replaced subtree nor on the way to it.
        if (relativePath is null && !KVPath.IsSameOrDescendant(nodePath, replacedPath)) return;

        foreach (var nodeDefinition in node.Definition.Nodes)
        {
            NotifyReplaced(state, nodeDefinition.GetChildNode(node), replacedPath);
        }

        foreach (var collectionDefinition in node.Definition.Collections)
        {
            var collection = collectionDefinition.GetCollection(node);
            foreach (var itemId in collection.GetActiveItemIds())
            {
                if (collection.GetById(itemId) is { } item)
                {
                    NotifyReplaced(state, item, replacedPath);
                }
            }
        }

        foreach (var nestedDefinition in node.Definition.NestedNodes)
        {
            var nestedModel = node.GetNestedNodeModel(nestedDefinition.SubSegmentPath);
            if (node.GetActiveNestedNode(nestedDefinition, nestedModel) is { } nestedNode)
            {
                NotifyReplaced(state, nestedNode, replacedPath);
            }
        }

        // A node inside the replaced subtree was replaced as a whole: every one of its reactions applies.
        var changedPath = relativePath ?? string.Empty;
        foreach (var reaction in node.Definition.ChangeReactions)
        {
            if (reaction.Pattern.MatchesAtOrUnder(changedPath))
            {
                InvokeReaction(state, node, nodePath, reaction, changedPath, oldValue: null, newValue: null, KVChangeKind.Replaced);
            }
        }
    }

    private static KVNode? ParentNode(KVNode node) => node.Parent switch
    {
        KVNode parentNode => parentNode,
        IKVCollectionNode { Parent: KVNode collectionParent } => collectionParent,
        _ => null
    };

    private static void InvokeReaction(
        KVReactionExecutionState state,
        KVNode node,
        string nodeCanonicalPath,
        KVChangeReactionDescriptor reaction,
        string changedPath,
        object? oldValue,
        object? newValue,
        KVChangeKind kind)
    {
        if (state.Stack.Count >= MaxReactionChainLength)
        {
            throw new InvalidOperationException($"Change reaction chain exceeded {MaxReactionChainLength} frames. Check for runaway change reactions involving '{changedPath}'.");
        }

        var frame = new KVReactionFrame(nodeCanonicalPath, changedPath, reaction);
        if (!state.ActiveFrames.Add(frame))
        {
            throw new InvalidOperationException($"Cyclic change reaction detected for '{changedPath}' on '{nodeCanonicalPath}'.");
        }

        state.Stack.Push(frame);
        try
        {
            reaction.Invoke(node, changedPath, oldValue, newValue, kind);
        }
        finally
        {
            state.Stack.Pop();
            state.ActiveFrames.Remove(frame);
        }
    }

    internal static KVRootNode ResolveRoot(KVNode source)
    {
        KVNode? current = source;
        while (current is not null)
        {
            if (current is KVRootNode root)
            {
                return root;
            }

            current = ParentNode(current);
        }

        throw new InvalidOperationException($"'{source.GetType().Name}' is not bound under a root aggregate.");
    }
}

internal sealed class KVReactionExecutionState
{
    private int scopeDepth;

    internal Stack<KVReactionFrame> Stack { get; } = [];

    internal HashSet<KVReactionFrame> ActiveFrames { get; } = [];

    internal bool EnterScope()
    {
        return this.scopeDepth++ == 0;
    }

    internal void ExitScope(bool isTopLevel)
    {
        this.scopeDepth--;
        if (isTopLevel)
        {
            this.Stack.Clear();
            this.ActiveFrames.Clear();
            this.scopeDepth = 0;
        }
    }
}
