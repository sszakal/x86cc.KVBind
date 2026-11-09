using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public enum KVCollectionAggregateComparison
{
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

internal interface IKVFieldValidationRuleFactory<TValue>
{
    IReadOnlyList<KVCompiledValidationRule> Build();
}

internal interface IKVCollectionValidationRuleFactory
{
    IReadOnlyList<KVCompiledValidationRule> Build();
}

public sealed record KVCompiledValidationRule(
    Func<KVValidationProfile, bool> ProfileMatches,
    Action<KVNode, string, string, List<KVValidationError>> Evaluate);

public abstract record KVValidationProfile;

public sealed record KVDefaultValidationProfile : KVValidationProfile
{
    private KVDefaultValidationProfile()
    {
    }

    public static KVDefaultValidationProfile Instance { get; } = new();
}

public sealed record KVValidationRegistration(
    string ScopePath,
    bool IncludeDescendants,
    IReadOnlyList<KVCompiledValidationRule> Rules);

public sealed record KVCollectionAggregateRule(string FieldKey, decimal Threshold, KVCollectionAggregateComparison Comparison, string ErrorCode);

public sealed class KVFieldValidationProfileBuilder<TValue> : IKVFieldValidationRuleFactory<TValue>
{
    private readonly List<KVCompiledValidationRule> rules = [];

    public KVFieldValidationProfileBuilder<TValue> For<TProfile>(Action<KVFieldValueRuleBuilder<TValue>> configure)
        where TProfile : KVValidationProfile
    {
        ArgumentNullException.ThrowIfNull(configure);

        var rules = new KVFieldValueRuleBuilder<TValue>();
        configure(rules);
        this.rules.Add(new KVCompiledValidationRule(
            current => current is TProfile,
            (node, path, currentCanonicalPath, errors) =>
            {
                var storagePath = node.ResolveStoragePathForCanonicalPath(path, currentCanonicalPath);
                var value = node.Model.Get<object?>(storagePath);
                rules.Evaluate(path, value, errors);
            }));
        return this;
    }

    IReadOnlyList<KVCompiledValidationRule> IKVFieldValidationRuleFactory<TValue>.Build()
    {
        return this.rules;
    }
}

public sealed class KVFieldValueRuleBuilder<TValue>
{
    private bool required;
    private int? maxLength;

    public KVFieldValueRuleBuilder<TValue> Required()
    {
        this.required = true;
        return this;
    }

    public KVFieldValueRuleBuilder<TValue> MaxLength(int maxLength)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "MaxLength must be greater than zero.");
        }

        this.maxLength = maxLength;
        return this;
    }

    internal void Evaluate(string path, object? value, List<KVValidationError> errors)
    {
        if (this.required)
        {
            if (value is null || (value is string requiredText && string.IsNullOrWhiteSpace(requiredText)))
            {
                errors.Add(new KVValidationError(path, "required", $"'{path}' is required."));
                return;
            }
        }

        if (this.maxLength.HasValue && value is string text && text.Length > this.maxLength.Value)
        {
            errors.Add(new KVValidationError(path, "max_length", $"'{path}' must be at most {this.maxLength.Value} characters."));
        }
    }
}

public sealed class IkvCollectionValidationProfileBuilder<TModel> : IKVCollectionValidationRuleFactory
    where TModel : KVCollectionItemNode, new()
{
    private readonly List<KVCompiledValidationRule> rules = [];

    public IkvCollectionValidationProfileBuilder<TModel> For<TProfile>(Action<KVCollectionRuleBuilder<TModel>> configure)
        where TProfile : KVValidationProfile
    {
        ArgumentNullException.ThrowIfNull(configure);

        var rules = new KVCollectionRuleBuilder<TModel>();
        configure(rules);
        this.rules.Add(new KVCompiledValidationRule(
            current => current is TProfile,
            (node, path, currentCanonicalPath, errors) => rules.Evaluate(path, node, currentCanonicalPath, errors)));
        return this;
    }

    IReadOnlyList<KVCompiledValidationRule> IKVCollectionValidationRuleFactory.Build()
    {
        return this.rules;
    }
}

public sealed class KVCollectionRuleBuilder<TModel>
    where TModel : KVCollectionItemNode, new()
{
    private readonly List<KVCollectionAggregateRule> aggregateRules = [];

    private bool notEmpty;

    private int? minCount;

    private int? maxCount;

    internal bool NotEmptyRule => this.notEmpty;

    internal int? MinCountValue => this.minCount;

    internal int? MaxCountValue => this.maxCount;

    internal IReadOnlyList<KVCollectionAggregateRule> AggregateRules => this.aggregateRules;

    public KVCollectionRuleBuilder<TModel> NotEmpty()
    {
        this.notEmpty = true;
        if (!this.minCount.HasValue || this.minCount.Value < 1)
        {
            this.minCount = 1;
        }

        return this;
    }

    public KVCollectionRuleBuilder<TModel> MinCount(int minCount)
    {
        if (minCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minCount), "MinCount cannot be negative.");
        }

        this.minCount = minCount;
        return this;
    }

    public KVCollectionRuleBuilder<TModel> MaxCount(int maxCount)
    {
        if (maxCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "MaxCount cannot be negative.");
        }

        this.maxCount = maxCount;
        return this;
    }

    public KVCollectionAggregateRuleBuilder<TModel> AggregateSum(string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            throw new ArgumentException("Aggregate field key is required.", nameof(fieldKey));
        }

        return new KVCollectionAggregateRuleBuilder<TModel>(this, fieldKey);
    }

    public KVCollectionAggregateRuleBuilder<TModel> AggregateSum<TItem, TValue>(Expression<Func<TItem, TValue>> selector)
        where TItem : KVCollectionItemNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : selector.Body;
        if (body is not MemberExpression memberExpression || memberExpression.Member is not PropertyInfo property)
        {
            throw new ArgumentException("Aggregate selector must target a property.", nameof(selector));
        }

        // The stored key is the [KVBind] canonical key, which may differ from the C# property name.
        var fieldKey = property.GetCustomAttribute<KVBindAttribute>()?.CanonicalKey ?? property.Name;
        return new KVCollectionAggregateRuleBuilder<TModel>(this, fieldKey);
    }

    internal IReadOnlyList<KVCompiledValidationRule> BuildGlobalRules()
    {
        if (!this.notEmpty && !this.minCount.HasValue && !this.maxCount.HasValue && this.aggregateRules.Count == 0)
        {
            return [];
        }

        return
        [
            new KVCompiledValidationRule(
                _ => true,
                (node, path, currentCanonicalPath, errors) => this.Evaluate(path, node, currentCanonicalPath, errors))
        ];
    }

    internal void Evaluate(string path, KVNode node, string currentCanonicalPath, List<KVValidationError> errors)
    {
        var collectionKey = node.ResolveStoragePathForCanonicalPath(path, currentCanonicalPath);
        var collectionDef = node.Definition.FindCollection(collectionKey);
        var collectionNode = collectionDef?.GetCollection(node);
        var children = collectionNode?.GetActiveItemIds() ?? [];
        var count = children.Count;

        if (this.notEmpty && count == 0)
        {
            errors.Add(new KVValidationError(path, "not_empty", $"'{path}' collection cannot be empty."));
        }

        if (this.minCount.HasValue && count < this.minCount.Value)
        {
            errors.Add(new KVValidationError(path, "min_count", $"'{path}' collection must contain at least {this.minCount.Value} item(s)."));
        }

        if (this.maxCount.HasValue && count > this.maxCount.Value)
        {
            errors.Add(new KVValidationError(path, "max_count", $"'{path}' collection must contain at most {this.maxCount.Value} item(s)."));
        }

        foreach (var aggregateRule in this.aggregateRules)
        {
            KVValidationRuntime.ValidateAggregateRule(errors, path, collectionNode, children, aggregateRule);
        }
    }

    internal KVCollectionRuleBuilder<TModel> AddAggregateRule(KVCollectionAggregateRule rule)
    {
        this.aggregateRules.Add(rule);
        return this;
    }

}

public sealed class KVCollectionAggregateRuleBuilder<TModel>
    where TModel : KVCollectionItemNode, new()
{
    private readonly KVCollectionRuleBuilder<TModel> owner;
    private readonly string fieldKey;

    internal KVCollectionAggregateRuleBuilder(KVCollectionRuleBuilder<TModel> owner, string fieldKey)
    {
        this.owner = owner;
        this.fieldKey = fieldKey;
    }

    public KVCollectionRuleBuilder<TModel> LessThan(decimal threshold)
        => this.Add(threshold, KVCollectionAggregateComparison.LessThan, "aggregate_less_than");

    public KVCollectionRuleBuilder<TModel> LessThanOrEqual(decimal threshold)
        => this.Add(threshold, KVCollectionAggregateComparison.LessThanOrEqual, "aggregate_less_than_or_equal");

    public KVCollectionRuleBuilder<TModel> GreaterThan(decimal threshold)
        => this.Add(threshold, KVCollectionAggregateComparison.GreaterThan, "aggregate_greater_than");

    public KVCollectionRuleBuilder<TModel> GreaterThanOrEqual(decimal threshold)
        => this.Add(threshold, KVCollectionAggregateComparison.GreaterThanOrEqual, "aggregate_greater_than_or_equal");

    private KVCollectionRuleBuilder<TModel> Add(decimal threshold, KVCollectionAggregateComparison comparison, string code)
    {
        return this.owner.AddAggregateRule(new KVCollectionAggregateRule(this.fieldKey, threshold, comparison, code));
    }
}

public sealed class KVGroupValidationProfileBuilder<TNode>
{
    private readonly Func<LambdaExpression, string> resolveFieldKey;
    private readonly List<KVCompiledValidationRule> rules = [];

    internal KVGroupValidationProfileBuilder(Func<LambdaExpression, string> resolveFieldKey)
    {
        this.resolveFieldKey = resolveFieldKey;
    }

    public KVGroupValidationProfileBuilder<TNode> For<TProfile>(Action<KVGroupRuleBuilder<TNode>> configure)
        where TProfile : KVValidationProfile
    {
        ArgumentNullException.ThrowIfNull(configure);

        var rules = new KVGroupRuleBuilder<TNode>(this.resolveFieldKey);
        configure(rules);
        this.rules.Add(new KVCompiledValidationRule(
            current => current is TProfile,
            (node, _, currentCanonicalPath, errors) => rules.Evaluate(node, currentCanonicalPath, errors)));
        return this;
    }

    internal IReadOnlyList<KVCompiledValidationRule> Build()
    {
        return this.rules;
    }
}

public sealed class KVGroupRuleBuilder<TNode>
{
    private readonly Func<LambdaExpression, string> resolveFieldKey;
    private readonly List<Action<KVGroupValueAccessor<TNode>, List<KVValidationError>>> steps = [];

    internal KVGroupRuleBuilder(Func<LambdaExpression, string> resolveFieldKey)
    {
        this.resolveFieldKey = resolveFieldKey;
    }

    public KVGroupRuleBuilder<TNode> Required<TValue>(Expression<Func<TNode, TValue>> fieldSelector)
    {
        ArgumentNullException.ThrowIfNull(fieldSelector);

        var fieldKey = this.resolveFieldKey(fieldSelector);
        this.steps.Add((accessor, errors) =>
        {
            var value = accessor.GetRaw(fieldKey);
            if (value is null || (value is string text && string.IsNullOrWhiteSpace(text)))
            {
                var path = accessor.ResolveCanonicalPath(fieldKey);
                errors.Add(new KVValidationError(path, "required", $"'{path}' is required."));
            }
        });

        return this;
    }

    public KVGroupRuleBuilder<TNode> MaxLength(Expression<Func<TNode, string?>> fieldSelector, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(fieldSelector);
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "MaxLength must be greater than zero.");
        }

        var fieldKey = this.resolveFieldKey(fieldSelector);
        this.steps.Add((accessor, errors) =>
        {
            var value = accessor.GetRaw(fieldKey);
            if (value is string text && text.Length > maxLength)
            {
                var path = accessor.ResolveCanonicalPath(fieldKey);
                errors.Add(new KVValidationError(path, "max_length", $"'{path}' must be at most {maxLength} characters."));
            }
        });

        return this;
    }

    public KVGroupRuleBuilder<TNode> When(
        Func<KVGroupValueAccessor<TNode>, bool> condition,
        Action<KVGroupRuleBuilder<TNode>> configure)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(configure);

        var nested = new KVGroupRuleBuilder<TNode>(this.resolveFieldKey);
        configure(nested);
        this.steps.Add((accessor, errors) =>
        {
            if (condition(accessor))
            {
                nested.Evaluate(accessor, errors);
            }
        });

        return this;
    }

    public KVGroupRuleBuilder<TNode> Custom(Action<KVGroupValueAccessor<TNode>, List<KVValidationError>> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        this.steps.Add(validate);
        return this;
    }

    internal IReadOnlyList<KVCompiledValidationRule> BuildGlobalRules()
    {
        if (this.steps.Count == 0)
        {
            return [];
        }

        return
        [
            new KVCompiledValidationRule(
                _ => true,
                (node, _, currentCanonicalPath, errors) => this.Evaluate(node, currentCanonicalPath, errors))
        ];
    }

    internal void Evaluate(KVNode node, string currentCanonicalPath, List<KVValidationError> errors)
    {
        var accessor = new KVGroupValueAccessor<TNode>(node, currentCanonicalPath, this.resolveFieldKey);
        this.Evaluate(accessor, errors);
    }

    private void Evaluate(KVGroupValueAccessor<TNode> accessor, List<KVValidationError> errors)
    {
        foreach (var step in this.steps)
        {
            step(accessor, errors);
        }
    }
}

public sealed class KVGroupValueAccessor<TNode>
{
    private readonly KVNode node;

    private readonly string currentCanonicalPath;

    private readonly Func<LambdaExpression, string> resolveFieldKey;

    internal KVGroupValueAccessor(KVNode node, string currentCanonicalPath, Func<LambdaExpression, string> resolveFieldKey)
    {
        this.node = node;
        this.currentCanonicalPath = currentCanonicalPath;
        this.resolveFieldKey = resolveFieldKey;
    }

    public TValue? Get<TValue>(Expression<Func<TNode, TValue>> fieldSelector)
    {
        ArgumentNullException.ThrowIfNull(fieldSelector);
        var fieldKey = this.resolveFieldKey(fieldSelector);
        var storagePath = this.ResolveStoragePath(fieldKey);
        var raw = this.node.Model.Get<object?>(storagePath);
        if (raw is null)
        {
            return default;
        }

        if (raw is TValue typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Field '{this.ResolveCanonicalPath(fieldKey)}' contains value type '{raw.GetType().FullName}', which cannot be cast to '{typeof(TValue).FullName}'.");
    }

    public bool HasValue<TValue>(Expression<Func<TNode, TValue>> fieldSelector)
    {
        var value = this.Get(fieldSelector);
        return value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    public object? Get(string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            throw new ArgumentException("Field key is required.", nameof(fieldKey));
        }

        return this.GetRaw(fieldKey);
    }

    public IReadOnlyList<string> GetCollectionChildIds(string collectionPath)
    {
        if (string.IsNullOrWhiteSpace(collectionPath))
        {
            throw new ArgumentException("Collection path is required.", nameof(collectionPath));
        }

        var collectionKey = this.ResolveStoragePath(collectionPath);
        var collectionDef = this.node.Definition.FindCollection(collectionKey);
        if (collectionDef is null) return [];

        var collectionNode = collectionDef.GetCollection(this.node);
        return collectionNode?.GetActiveItemIds() ?? [];
    }

    internal object? GetRaw(string fieldKey)
    {
        var storagePath = this.ResolveStoragePath(fieldKey);
        return this.node.Model.Get<object?>(storagePath);
    }

    internal string ResolveCanonicalPath(string fieldKey)
    {
        var normalizedFieldKey = KVPath.NormalizeRelative(fieldKey);

        if (string.IsNullOrWhiteSpace(this.currentCanonicalPath))
        {
            return normalizedFieldKey;
        }

        if (string.Equals(normalizedFieldKey, this.currentCanonicalPath, StringComparison.Ordinal)
            || KVPath.IsSameOrDescendant(normalizedFieldKey, this.currentCanonicalPath))
        {
            return normalizedFieldKey;
        }

        return KVPath.Combine(this.currentCanonicalPath, normalizedFieldKey);
    }

    private string ResolveStoragePath(string fieldKey)
    {
        return this.node.ResolveStoragePathForCanonicalPath(this.ResolveCanonicalPath(fieldKey), this.currentCanonicalPath);
    }
}
