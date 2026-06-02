using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public sealed class KVChangeContext<TNode>
    where TNode : KVNode
{
    internal KVChangeContext(TNode node, string changedPath, object? oldValue, object? newValue)
    {
        Node = node;
        ChangedPath = changedPath;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public TNode Node { get; }

    public string ChangedPath { get; }

    public object? OldValue { get; }

    public object? NewValue { get; }
}

internal sealed class KVChangeReactionDescriptor
{
    private readonly Action<KVNode, string, object?, object?> _invoke;

    private KVChangeReactionDescriptor(KVPathPattern pattern, Action<KVNode, string, object?, object?> invoke)
    {
        Pattern = pattern;
        _invoke = invoke;
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
            (node, changedPath, oldValue, newValue) =>
            {
                var typedNode = (TNode)node;
                selector(typedNode)(new KVChangeContext<TNode>(typedNode, changedPath, oldValue, newValue));
            });
    }

    internal void Invoke(KVNode node, string changedPath, object? oldValue, object? newValue)
    {
        _invoke(node, changedPath, oldValue, newValue);
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

public sealed class KVChangePath
{
    internal KVChangePath(KVPathPattern pattern)
    {
        Pattern = pattern;
    }

    internal KVPathPattern Pattern { get; }
}

internal sealed class KVPathPattern
{
    private readonly IReadOnlyList<string> _segments;

    internal KVPathPattern(IReadOnlyList<string> segments)
    {
        _segments = segments;
    }

    internal bool Matches(string path)
    {
        var segments = KVPath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != _segments.Count)
        {
            return false;
        }

        for (var i = 0; i < _segments.Count; i++)
        {
            if (_segments[i] == "*")
            {
                continue;
            }

            if (!string.Equals(_segments[i], segments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class KVChangePathBuilder<TNode>
    where TNode : KVNode
{
    private readonly List<string> _segments;
    private readonly Func<LambdaExpression, string> _resolveSelectorKey;

    internal KVChangePathBuilder(Func<LambdaExpression, string> resolveSelectorKey)
        : this([], resolveSelectorKey)
    {
    }

    private KVChangePathBuilder(List<string> segments, Func<LambdaExpression, string> resolveSelectorKey)
    {
        _segments = segments;
        _resolveSelectorKey = resolveSelectorKey;
    }

    public KVChangePathBuilder<TItem> Collection<TItem>(Expression<Func<TNode, KVCollectionNode<TItem>>> selector)
        where TItem : KVCollectionItemNode, new()
    {
        ArgumentNullException.ThrowIfNull(selector);
        var segments = new List<string>(_segments)
        {
            _resolveSelectorKey(selector),
            "*"
        };
        return new KVChangePathBuilder<TItem>(segments, _resolveSelectorKey);
    }

    public KVChangePathBuilder<TNested> NestedNode<TNested>(Expression<Func<TNode, TNested?>> selector)
        where TNested : KVNestedNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var segments = new List<string>(_segments)
        {
            _resolveSelectorKey(selector)
        };
        return new KVChangePathBuilder<TNested>(segments, _resolveSelectorKey);
    }

    public KVChangePath Field<TValue>(Expression<Func<TNode, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var segments = new List<string>(_segments)
        {
            _resolveSelectorKey(selector)
        };
        return new KVChangePath(new KVPathPattern(segments));
    }

    public KVChangePath Any()
    {
        return new KVChangePath(new KVPathPattern([.. _segments]));
    }
}
