using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

internal static class KVChangeReactionRuntime
{
    private const int MaxReactionChainLength = 16;

    internal static void Emit(KVNode source, string canonicalPath, object? oldValue, object? newValue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(canonicalPath);

        var root = ResolveRoot(source);
        var state = root.ReactionExecutionState;
        var isTopLevel = state.EnterScope();

        try
        {
            foreach (var ancestor in EnumerateAncestorNodes(source))
            {
                var ancestorPath = ancestor.GetCanonicalPath();
                var relativePath = KVPath.RelativeTo(canonicalPath, ancestorPath);
                if (relativePath is null)
                {
                    continue;
                }

                foreach (var reaction in ancestor.Definition.ChangeReactions)
                {
                    if (reaction.Pattern.Matches(relativePath))
                    {
                        InvokeReaction(state, ancestor, ancestorPath, reaction, relativePath, oldValue, newValue);
                    }
                }
            }
        }
        finally
        {
            state.ExitScope(isTopLevel);
        }
    }

    private static void InvokeReaction(
        KVReactionExecutionState state,
        KVNode node,
        string nodeCanonicalPath,
        KVChangeReactionDescriptor reaction,
        string changedPath,
        object? oldValue,
        object? newValue)
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
            reaction.Invoke(node, changedPath, oldValue, newValue);
        }
        finally
        {
            state.Stack.Pop();
            state.ActiveFrames.Remove(frame);
        }
    }

    private static KVRootNode ResolveRoot(KVNode source)
    {
        KVNode? current = source;
        while (current is not null)
        {
            if (current is KVRootNode root)
            {
                return root;
            }

            current = current.Parent switch
            {
                KVNode parentNode => parentNode,
                IKVCollectionNode { Parent: KVNode collectionParent } => collectionParent,
                _ => null
            };
        }

        throw new InvalidOperationException("Change reactions require a bound root node.");
    }

    private static IEnumerable<KVNode> EnumerateAncestorNodes(KVNode source)
    {
        KVNode? current = source;
        while (current is not null)
        {
            yield return current;
            current = current.Parent switch
            {
                KVNode parentNode => parentNode,
                IKVCollectionNode { Parent: KVNode collectionParent } => collectionParent,
                _ => null
            };
        }
    }
}

internal sealed class KVReactionExecutionState
{
    private int _scopeDepth;

    internal Stack<KVReactionFrame> Stack { get; } = [];

    internal HashSet<KVReactionFrame> ActiveFrames { get; } = [];

    internal bool EnterScope()
    {
        return _scopeDepth++ == 0;
    }

    internal void ExitScope(bool isTopLevel)
    {
        _scopeDepth--;
        if (isTopLevel)
        {
            Stack.Clear();
            ActiveFrames.Clear();
            _scopeDepth = 0;
        }
    }
}

internal readonly record struct KVReactionFrame(
    string NodeCanonicalPath,
    string ChangedPath,
    KVChangeReactionDescriptor Reaction);
