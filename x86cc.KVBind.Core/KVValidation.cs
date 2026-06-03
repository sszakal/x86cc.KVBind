using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public sealed record KVCompiledValidationRule(
    Func<KVValidationProfile, bool> ProfileMatches,
    Action<KVNode, string, string, List<KVValidationError>> Evaluate);

public abstract record KVValidationProfile;

public sealed record KVDefaultValidationProfile : KVValidationProfile
{
    public static KVDefaultValidationProfile Instance { get; } = new();

    private KVDefaultValidationProfile()
    {
    }
}

public sealed record KVValidationRegistration(
    string ScopePath,
    bool IncludeDescendants,
    IReadOnlyList<KVCompiledValidationRule> Rules);

public enum KVCollectionAggregateComparison
{
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

public sealed record KVCollectionAggregateRule(string FieldKey, decimal Threshold, KVCollectionAggregateComparison Comparison, string ErrorCode);

internal interface IKVFieldValidationRuleFactory<TValue>
{
    IReadOnlyList<KVCompiledValidationRule> Build();
}

public sealed class KVFieldValidationProfileBuilder<TValue> : IKVFieldValidationRuleFactory<TValue>
{
    private readonly List<KVCompiledValidationRule> _rules = [];

    public KVFieldValidationProfileBuilder<TValue> For<TProfile>(Action<KVFieldValueRuleBuilder<TValue>> configure)
        where TProfile : KVValidationProfile
    {
        ArgumentNullException.ThrowIfNull(configure);

        var rules = new KVFieldValueRuleBuilder<TValue>();
        configure(rules);
        _rules.Add(new KVCompiledValidationRule(
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
        return _rules;
    }
}

public sealed class KVFieldValueRuleBuilder<TValue>
{
    private bool _required;
    private int? _maxLength;

    public KVFieldValueRuleBuilder<TValue> Required()
    {
        _required = true;
        return this;
    }

    public KVFieldValueRuleBuilder<TValue> MaxLength(int maxLength)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength), "MaxLength must be greater than zero.");
        }

        _maxLength = maxLength;
        return this;
    }

    internal void Evaluate(string path, object? value, List<KVValidationError> errors)
    {
        if (_required)
        {
            if (value is null || (value is string requiredText && string.IsNullOrWhiteSpace(requiredText)))
            {
                errors.Add(new KVValidationError(path, "required", $"'{path}' is required."));
                return;
            }
        }

        if (_maxLength.HasValue && value is string text && text.Length > _maxLength.Value)
        {
            errors.Add(new KVValidationError(path, "max_length", $"'{path}' must be at most {_maxLength.Value} characters."));
        }
    }
}

internal interface IKVCollectionValidationRuleFactory
{
    IReadOnlyList<KVCompiledValidationRule> Build();
}

public sealed class IkvCollectionValidationProfileBuilder<TModel> : IKVCollectionValidationRuleFactory
    where TModel : KVCollectionItemNode, new()
{
    private readonly List<KVCompiledValidationRule> _rules = [];

    public IkvCollectionValidationProfileBuilder<TModel> For<TProfile>(Action<KVCollectionRuleBuilder<TModel>> configure)
        where TProfile : KVValidationProfile
    {
        ArgumentNullException.ThrowIfNull(configure);

        var rules = new KVCollectionRuleBuilder<TModel>();
        configure(rules);
        _rules.Add(new KVCompiledValidationRule(
            current => current is TProfile,
            (node, path, currentCanonicalPath, errors) => rules.Evaluate(path, node, currentCanonicalPath, errors)));
        return this;
    }

    IReadOnlyList<KVCompiledValidationRule> IKVCollectionValidationRuleFactory.Build()
    {
        return _rules;
    }
}

public sealed class KVCollectionRuleBuilder<TModel>
    where TModel : KVCollectionItemNode, new()
{
    private bool _notEmpty;
    private int? _minCount;
    private int? _maxCount;
    private readonly List<KVCollectionAggregateRule> _aggregateRules = [];

    internal bool NotEmptyRule => _notEmpty;

    internal int? MinCountValue => _minCount;

    internal int? MaxCountValue => _maxCount;

    internal IReadOnlyList<KVCollectionAggregateRule> AggregateRules => _aggregateRules;

    public KVCollectionRuleBuilder<TModel> NotEmpty()
    {
        _notEmpty = true;
        if (!_minCount.HasValue || _minCount.Value < 1)
        {
            _minCount = 1;
        }

        return this;
    }

    public KVCollectionRuleBuilder<TModel> MinCount(int minCount)
    {
        if (minCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minCount), "MinCount cannot be negative.");
        }

        _minCount = minCount;
        return this;
    }

    public KVCollectionRuleBuilder<TModel> MaxCount(int maxCount)
    {
        if (maxCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "MaxCount cannot be negative.");
        }

        _maxCount = maxCount;
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
        if (selector.Body is not MemberExpression memberExpression || memberExpression.Member is not PropertyInfo property)
        {
            throw new ArgumentException("Aggregate selector must target a property.", nameof(selector));
        }

        var fieldKey = property.Name;
        return new KVCollectionAggregateRuleBuilder<TModel>(this, fieldKey);
    }

    internal IReadOnlyList<KVCompiledValidationRule> BuildGlobalRules()
    {
        if (!_notEmpty && !_minCount.HasValue && !_maxCount.HasValue && _aggregateRules.Count == 0)
        {
            return [];
        }

        return
        [
            new KVCompiledValidationRule(
                _ => true,
                (node, path, currentCanonicalPath, errors) => Evaluate(path, node, currentCanonicalPath, errors))
        ];
    }

    internal void Evaluate(string path, KVNode node, string currentCanonicalPath, List<KVValidationError> errors)
    {
        var collectionKey = node.ResolveStoragePathForCanonicalPath(path, currentCanonicalPath);
        var collectionDef = node.Definition.Collections.Find(c =>
            string.Equals(c.SubSegmentPath, collectionKey, StringComparison.Ordinal));
        var collectionNode = collectionDef?.GetCollection(node);
        var children = collectionNode?.GetActiveItemIds() ?? [];
        var count = children.Count;

        if (_notEmpty && count == 0)
        {
            errors.Add(new KVValidationError(path, "not_empty", $"'{path}' collection cannot be empty."));
        }

        if (_minCount.HasValue && count < _minCount.Value)
        {
            errors.Add(new KVValidationError(path, "min_count", $"'{path}' collection must contain at least {_minCount.Value} item(s)."));
        }

        if (_maxCount.HasValue && count > _maxCount.Value)
        {
            errors.Add(new KVValidationError(path, "max_count", $"'{path}' collection must contain at most {_maxCount.Value} item(s)."));
        }

        foreach (var aggregateRule in _aggregateRules)
        {
            decimal sum = 0m;
            foreach (var child in children)
            {
                var itemNode = collectionNode?.GetById(child) as KVNode;
                if (itemNode is null) continue;

                var rawValue = itemNode.Model.Get<object?>(aggregateRule.FieldKey);
                if (rawValue is null) continue;

                try
                {
                    sum += Convert.ToDecimal(rawValue);
                }
                catch
                {
                }
            }

            if (!Compare(sum, aggregateRule.Threshold, aggregateRule.Comparison))
            {
                errors.Add(new KVValidationError(path, aggregateRule.ErrorCode, $"'{path}' aggregate sum for '{aggregateRule.FieldKey}' is invalid."));
            }
        }
    }

    private static bool Compare(decimal value, decimal threshold, KVCollectionAggregateComparison comparison)
    {
        return comparison switch
        {
            KVCollectionAggregateComparison.LessThan => value < threshold,
            KVCollectionAggregateComparison.LessThanOrEqual => value <= threshold,
            KVCollectionAggregateComparison.GreaterThan => value > threshold,
            KVCollectionAggregateComparison.GreaterThanOrEqual => value >= threshold,
            _ => false
        };
    }

    internal KVCollectionRuleBuilder<TModel> AddAggregateRule(KVCollectionAggregateRule rule)
    {
        _aggregateRules.Add(rule);
        return this;
    }
}

public sealed class KVCollectionAggregateRuleBuilder<TModel>
    where TModel : KVCollectionItemNode, new()
{
    private readonly KVCollectionRuleBuilder<TModel> _owner;
    private readonly string _fieldKey;

    internal KVCollectionAggregateRuleBuilder(KVCollectionRuleBuilder<TModel> owner, string fieldKey)
    {
        _owner = owner;
        _fieldKey = fieldKey;
    }

    public KVCollectionRuleBuilder<TModel> LessThan(decimal threshold)
        => Add(threshold, KVCollectionAggregateComparison.LessThan, "aggregate_less_than");

    public KVCollectionRuleBuilder<TModel> LessThanOrEqual(decimal threshold)
        => Add(threshold, KVCollectionAggregateComparison.LessThanOrEqual, "aggregate_less_than_or_equal");

    public KVCollectionRuleBuilder<TModel> GreaterThan(decimal threshold)
        => Add(threshold, KVCollectionAggregateComparison.GreaterThan, "aggregate_greater_than");

    public KVCollectionRuleBuilder<TModel> GreaterThanOrEqual(decimal threshold)
        => Add(threshold, KVCollectionAggregateComparison.GreaterThanOrEqual, "aggregate_greater_than_or_equal");

    private KVCollectionRuleBuilder<TModel> Add(decimal threshold, KVCollectionAggregateComparison comparison, string code)
    {
        return _owner.AddAggregateRule(new KVCollectionAggregateRule(_fieldKey, threshold, comparison, code));
    }
}

public sealed class KVGroupValidationProfileBuilder<TNode>
{
    private readonly Func<LambdaExpression, string> _resolveFieldKey;
    private readonly List<KVCompiledValidationRule> _rules = [];

    internal KVGroupValidationProfileBuilder(Func<LambdaExpression, string> resolveFieldKey)
    {
        _resolveFieldKey = resolveFieldKey;
    }

    public KVGroupValidationProfileBuilder<TNode> For<TProfile>(Action<KVGroupRuleBuilder<TNode>> configure)
        where TProfile : KVValidationProfile
    {
        ArgumentNullException.ThrowIfNull(configure);

        var rules = new KVGroupRuleBuilder<TNode>(_resolveFieldKey);
        configure(rules);
        _rules.Add(new KVCompiledValidationRule(
            current => current is TProfile,
            (node, _, currentCanonicalPath, errors) => rules.Evaluate(node, currentCanonicalPath, errors)));
        return this;
    }

    internal IReadOnlyList<KVCompiledValidationRule> Build()
    {
        return _rules;
    }
}

public sealed class KVGroupRuleBuilder<TNode>
{
    private readonly Func<LambdaExpression, string> _resolveFieldKey;
    private readonly List<Action<KVGroupValueAccessor<TNode>, List<KVValidationError>>> _steps = [];

    internal KVGroupRuleBuilder(Func<LambdaExpression, string> resolveFieldKey)
    {
        _resolveFieldKey = resolveFieldKey;
    }

    public KVGroupRuleBuilder<TNode> Required<TValue>(Expression<Func<TNode, TValue>> fieldSelector)
    {
        ArgumentNullException.ThrowIfNull(fieldSelector);

        var fieldKey = _resolveFieldKey(fieldSelector);
        _steps.Add((accessor, errors) =>
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

        var fieldKey = _resolveFieldKey(fieldSelector);
        _steps.Add((accessor, errors) =>
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

        var nested = new KVGroupRuleBuilder<TNode>(_resolveFieldKey);
        configure(nested);
        _steps.Add((accessor, errors) =>
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
        _steps.Add(validate);
        return this;
    }

    internal IReadOnlyList<KVCompiledValidationRule> BuildGlobalRules()
    {
        if (_steps.Count == 0)
        {
            return [];
        }

        return
        [
            new KVCompiledValidationRule(
                _ => true,
                (node, _, currentCanonicalPath, errors) => Evaluate(node, currentCanonicalPath, errors))
        ];
    }

    internal void Evaluate(KVNode node, string currentCanonicalPath, List<KVValidationError> errors)
    {
        var accessor = new KVGroupValueAccessor<TNode>(node, currentCanonicalPath, _resolveFieldKey);
        Evaluate(accessor, errors);
    }

    private void Evaluate(KVGroupValueAccessor<TNode> accessor, List<KVValidationError> errors)
    {
        foreach (var step in _steps)
        {
            step(accessor, errors);
        }
    }
}

public sealed class KVGroupValueAccessor<TNode>
{
    private readonly KVNode _node;
    private readonly string _currentCanonicalPath;
    private readonly Func<LambdaExpression, string> _resolveFieldKey;

    internal KVGroupValueAccessor(KVNode node, string currentCanonicalPath, Func<LambdaExpression, string> resolveFieldKey)
    {
        _node = node;
        _currentCanonicalPath = currentCanonicalPath;
        _resolveFieldKey = resolveFieldKey;
    }

    public TValue? Get<TValue>(Expression<Func<TNode, TValue>> fieldSelector)
    {
        ArgumentNullException.ThrowIfNull(fieldSelector);
        var fieldKey = _resolveFieldKey(fieldSelector);
        var storagePath = ResolveStoragePath(fieldKey);
        var raw = _node.Model.Get<object?>(storagePath);
        if (raw is null)
        {
            return default;
        }

        if (raw is TValue typed)
        {
            return typed;
        }

        throw new InvalidOperationException($"Field '{ResolveCanonicalPath(fieldKey)}' contains value type '{raw.GetType().FullName}', which cannot be cast to '{typeof(TValue).FullName}'.");
    }

    public bool HasValue<TValue>(Expression<Func<TNode, TValue>> fieldSelector)
    {
        var value = Get(fieldSelector);
        return value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            _ => true
        };
    }

    internal object? GetRaw(string fieldKey)
    {
        var storagePath = ResolveStoragePath(fieldKey);
        return _node.Model.Get<object?>(storagePath);
    }

    public object? Get(string fieldKey)
    {
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            throw new ArgumentException("Field key is required.", nameof(fieldKey));
        }

        return GetRaw(fieldKey);
    }

    public IReadOnlyList<string> GetCollectionChildIds(string collectionPath)
    {
        if (string.IsNullOrWhiteSpace(collectionPath))
        {
            throw new ArgumentException("Collection path is required.", nameof(collectionPath));
        }

        var collectionKey = ResolveStoragePath(collectionPath);
        var collectionDef = _node.Definition.Collections.Find(c =>
            string.Equals(c.SubSegmentPath, collectionKey, StringComparison.Ordinal));
        if (collectionDef is null) return [];

        var collectionNode = collectionDef.GetCollection(_node);
        return collectionNode?.GetActiveItemIds() ?? [];
    }

    internal string ResolveCanonicalPath(string fieldKey)
    {
        var normalizedFieldKey = KVPath.NormalizeRelative(fieldKey);

        if (string.IsNullOrWhiteSpace(_currentCanonicalPath))
        {
            return normalizedFieldKey;
        }

        if (string.Equals(normalizedFieldKey, _currentCanonicalPath, StringComparison.Ordinal)
            || KVPath.IsSameOrDescendant(normalizedFieldKey, _currentCanonicalPath))
        {
            return normalizedFieldKey;
        }

        return KVPath.Combine(_currentCanonicalPath, normalizedFieldKey);
    }

    private string ResolveStoragePath(string fieldKey)
    {
        return _node.ResolveStoragePathForCanonicalPath(ResolveCanonicalPath(fieldKey), _currentCanonicalPath);
    }
}
