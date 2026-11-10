using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public sealed class KVChangeContext<TNode>
    where TNode : KVNode
{
    internal KVChangeContext(TNode node, string changedPath, object? oldValue, object? newValue, KVChangeKind kind)
    {
        this.Kind = kind;
        this.Node = node;
        this.ChangedPath = changedPath;
        this.OldValue = oldValue;
        this.NewValue = newValue;
    }

    public TNode Node { get; }

    public string ChangedPath { get; }

    public object? OldValue { get; }

    public object? NewValue { get; }

    // Field value change, or a structural one (item added/removed/moved, nested node initialized/dropped);
    // see KVChangeKind for what OldValue/NewValue hold in each case.
    public KVChangeKind Kind { get; }
}

public sealed class KVChangePath
{
    internal KVChangePath(KVPathPattern pattern)
    {
        this.Pattern = pattern;
    }

    internal KVPathPattern Pattern { get; }
}

public sealed class KVChangePathBuilder<TNode>
    where TNode : KVNode
{
    private readonly List<string> segments;
    private readonly Func<LambdaExpression, string> resolveSelectorKey;

    internal KVChangePathBuilder(Func<LambdaExpression, string> resolveSelectorKey)
        : this([], resolveSelectorKey)
    {
    }

    private KVChangePathBuilder(List<string> segments, Func<LambdaExpression, string> resolveSelectorKey)
    {
        this.segments = segments;
        this.resolveSelectorKey = resolveSelectorKey;
    }

    public KVChangePathBuilder<TItem> Collection<TItem>(Expression<Func<TNode, KVCollectionNode<TItem>>> selector)
        where TItem : KVCollectionItemNode, new()
    {
        ArgumentNullException.ThrowIfNull(selector);
        var segments = new List<string>(this.segments)
        {
            this.resolveSelectorKey(selector),
            "*"
        };
        return new KVChangePathBuilder<TItem>(segments, this.resolveSelectorKey);
    }

    public KVChangePathBuilder<TNested> NestedNode<TNested>(Expression<Func<TNode, TNested?>> selector)
        where TNested : KVNestedNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var segments = new List<string>(this.segments)
        {
            this.resolveSelectorKey(selector)
        };
        return new KVChangePathBuilder<TNested>(segments, this.resolveSelectorKey);
    }

    public KVChangePath Field<TValue>(Expression<Func<TNode, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var segments = new List<string>(this.segments)
        {
            this.resolveSelectorKey(selector)
        };
        return new KVChangePath(new KVPathPattern(segments));
    }

    public KVChangePath Any()
    {
        return new KVChangePath(new KVPathPattern([.. this.segments]));
    }
}

internal sealed class KVChangeReactionDescriptor
{
    private readonly Action<KVNode, string, object?, object?, KVChangeKind> invoke;

    private KVChangeReactionDescriptor(KVPathPattern pattern, Action<KVNode, string, object?, object?, KVChangeKind> invoke)
    {
        this.Pattern = pattern;
        this.invoke = invoke;
    }

    public KVPathPattern Pattern { get; }

    internal static KVChangeReactionDescriptor Create<TNode>(
        KVChangePath path,
        Expression<Func<TNode, Action<KVChangeContext<TNode>>>> action)
        where TNode : KVNode
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(action);

        var method = GetMethod(action);
        if (method.ReturnType != typeof(void))
        {
            throw new InvalidOperationException($"Change reaction method '{method.Name}' must return void.");
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(KVChangeContext<TNode>))
        {
            throw new InvalidOperationException($"Change reaction method '{method.Name}' must accept exactly one '{typeof(KVChangeContext<TNode>).FullName}' argument.");
        }

        var selector = action.Compile();
        return new KVChangeReactionDescriptor(
            path.Pattern,
            (node, changedPath, oldValue, newValue, kind) =>
            {
                var typedNode = (TNode)node;
                selector(typedNode)(new KVChangeContext<TNode>(typedNode, changedPath, oldValue, newValue, kind));
            });
    }

    internal void Invoke(KVNode node, string changedPath, object? oldValue, object? newValue, KVChangeKind kind)
    {
        this.invoke(node, changedPath, oldValue, newValue, kind);
    }

    private static MethodInfo GetMethod<TNode>(Expression<Func<TNode, Action<KVChangeContext<TNode>>>> action)
        where TNode : KVNode
    {
        Expression body = action.Body;
        if (body is UnaryExpression unaryExpression && unaryExpression.NodeType == ExpressionType.Convert)
        {
            body = unaryExpression.Operand;
        }

        if (body is MethodCallExpression methodCall
            && methodCall.Object is ConstantExpression { Value: MethodInfo methodInfo })
        {
            return methodInfo;
        }

        throw new InvalidOperationException("Change reaction registration must select an instance method group, for example x => x.ResetSummary.");
    }
}

internal sealed class KVPathPattern
{
    private readonly IReadOnlyList<string> segments;

    internal KVPathPattern(IReadOnlyList<string> segments)
    {
        this.segments = segments;
    }

    // True when the pattern watches `path` itself or something beneath it ("" covers everything).
    internal bool MatchesAtOrUnder(string path)
    {
        var segments = KVPath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > this.segments.Count)
        {
            return false;
        }

        for (var i = 0; i < segments.Length; i++)
        {
            if (this.segments[i] != "*" && !string.Equals(this.segments[i], segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    internal bool Matches(string path)
    {
        var segments = KVPath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != this.segments.Count)
        {
            return false;
        }

        for (var i = 0; i < this.segments.Count; i++)
        {
            if (this.segments[i] == "*")
            {
                continue;
            }

            if (!string.Equals(this.segments[i], segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
