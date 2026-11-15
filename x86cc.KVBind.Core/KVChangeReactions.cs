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

    // A field value change, or a structural change: an item added, removed or moved, or a nested node
    // initialized or dropped. KVChangeKind documents what the old and new value hold in each case.
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
        var pathSegments = new List<string>(this.segments)
        {
            this.resolveSelectorKey(selector),
            "*"
        };
        return new KVChangePathBuilder<TItem>(pathSegments, this.resolveSelectorKey);
    }

    public KVChangePathBuilder<TNested> NestedNode<TNested>(Expression<Func<TNode, TNested?>> selector)
        where TNested : KVNestedNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var pathSegments = new List<string>(this.segments)
        {
            this.resolveSelectorKey(selector)
        };
        return new KVChangePathBuilder<TNested>(pathSegments, this.resolveSelectorKey);
    }

    public KVChangePath Field<TValue>(Expression<Func<TNode, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var pathSegments = new List<string>(this.segments)
        {
            this.resolveSelectorKey(selector)
        };
        return new KVChangePath(new KVPathPattern(pathSegments));
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
    internal bool MatchesAtOrUnder(string path) => this.Match(path, exact: false);

    internal bool Matches(string path) => this.Match(path, exact: true);

    // Compares the path's segments with the pattern's in place, without splitting the path: this runs for
    // every reaction of every ancestor on every write. Empty segments are skipped, as a split with
    // RemoveEmptyEntries would. Exact: the path has as many segments as the pattern; otherwise it may
    // have fewer (a node the pattern watches something under).
    private bool Match(string path, bool exact)
    {
        var rest = path.AsSpan();
        var matched = 0;
        while (!rest.IsEmpty)
        {
            var slash = rest.IndexOf('/');
            var segment = slash < 0 ? rest : rest[..slash];
            rest = slash < 0 ? [] : rest[(slash + 1)..];
            if (segment.IsEmpty)
            {
                continue;
            }

            if (matched == this.segments.Count)
            {
                return false;
            }

            var expected = this.segments[matched];
            if (!string.Equals(expected, "*", StringComparison.Ordinal) && !segment.SequenceEqual(expected))
            {
                return false;
            }

            matched++;
        }

        return !exact || matched == this.segments.Count;
    }
}
