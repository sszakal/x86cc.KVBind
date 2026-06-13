using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.Core;

public sealed class KVCollectionOptionsBuilder<TParent, TModel>
    where TParent : KVNode
    where TModel : KVCollectionItemNode, new()
{
    private readonly KVCollectionRuleBuilder<TModel> _globalRules = new();
    private readonly List<IKVCollectionValidationRuleFactory> _validationFactories = [];
    private readonly List<KVCollectionItemDefinition> _itemDefinitions = [];
    private readonly List<KVPatchOperationDescriptor> _patchOperations = [];

    internal KVCollectionOptionsBuilder(Func<LambdaExpression, string> resolveSelectorKey)
    {
    }

    internal IReadOnlyList<KVCollectionItemDefinition> ItemDefinitions => _itemDefinitions;

    internal string? DisplayNameValue { get; private set; }

    private readonly Dictionary<string, object?> _annotations = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, object?> Annotations => _annotations;

    public KVCollectionOptionsBuilder<TParent, TModel> DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayNameValue = displayName;
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Annotate(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _annotations[key] = value;
        return this;
    }

    internal bool NotEmptyRule => _globalRules.NotEmptyRule;

    internal int? MinCountValue => _globalRules.MinCountValue;

    internal int? MaxCountValue => _globalRules.MaxCountValue;

    internal IReadOnlyList<KVCollectionAggregateRule> AggregateRules => _globalRules.AggregateRules;

    internal IReadOnlyList<KVCompiledValidationRule> ValidationRules { get; private set; } = [];

    internal IReadOnlyList<KVPatchOperationDescriptor> PatchOperations => _patchOperations;

    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>(Action<KVBindBuilder<TItem>> configure)
        where TItem : TModel, new()
    {
        return Item(typeof(TItem).Name, configure);
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>(string typeToken, Action<KVBindBuilder<TItem>> configure)
        where TItem : TModel, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new KVBindBuilder<TItem>();
        configure(builder);
        return RegisterItem<TItem>(typeToken, builder.Build());
    }

    // Self-describing item subtype: the item type carries its own definition via IKVNodeDefinition, so no
    // inline configuration is needed (enforced at compile time).
    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>()
        where TItem : TModel, IKVNodeDefinition, new()
        => Item<TItem>(typeof(TItem).Name);

    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>(string typeToken)
        where TItem : TModel, IKVNodeDefinition, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        return RegisterItem<TItem>(typeToken, TItem.Definition);
    }

    private KVCollectionOptionsBuilder<TParent, TModel> RegisterItem<TItem>(string typeToken, KVNodeDefinition nodeDefinition)
        where TItem : TModel, new()
    {
        if (typeof(KVRootNode).IsAssignableFrom(typeof(TItem)))
        {
            throw new InvalidOperationException($"Collection item type '{typeof(TItem).FullName}' cannot inherit KVRootNode.");
        }

        if (_itemDefinitions.Exists(definition => definition.ModelType == typeof(TItem)))
        {
            throw new InvalidOperationException($"Collection item type '{typeof(TItem).FullName}' is already declared.");
        }

        if (_itemDefinitions.Exists(definition => string.Equals(definition.TypeToken, typeToken, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Collection item token '{typeToken}' is already declared.");
        }

        _itemDefinitions.Add(new KVCollectionItemDefinition
        {
            ModelType = typeof(TItem),
            TypeToken = typeToken,
            NodeDefinition = nodeDefinition
        });

        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> NotEmpty()
    {
        _globalRules.NotEmpty();
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> MinCount(int minCount)
    {
        _globalRules.MinCount(minCount);
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> MaxCount(int maxCount)
    {
        _globalRules.MaxCount(maxCount);
        return this;
    }

    public KVCollectionAggregateRuleBuilder<TModel> AggregateSum(string fieldKey)
    {
        return _globalRules.AggregateSum(fieldKey);
    }

    public KVCollectionAggregateRuleBuilder<TModel> AggregateSum<TItem, TValue>(Expression<Func<TItem, TValue>> selector)
        where TItem : KVCollectionItemNode
    {
        return _globalRules.AggregateSum(selector);
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Validation(Action<IkvCollectionValidationProfileBuilder<TModel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new IkvCollectionValidationProfileBuilder<TModel>();
        configure(builder);
        _validationFactories.Add(builder);
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Operation<TArgument>(Expression<Func<TParent, Action<TArgument>>> methodSelector)
    {
        var operation = KVPatchOperationDescriptor.GetMethodName(methodSelector).ToUpperInvariant();
        return Operation(operation, methodSelector);
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Operation<TArgument>(string operation, Expression<Func<TParent, Action<TArgument>>> methodSelector)
    {
        var patchOperation = KVPatchOperationDescriptor.CustomCollection(operation, methodSelector);
        if (_patchOperations.Exists(existing => string.Equals(existing.Operation, patchOperation.Operation, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Patch operation '{patchOperation.Operation}' is already registered for this collection.");
        }

        _patchOperations.Add(patchOperation);
        return this;
    }

    internal void BuildValidationRules()
    {
        var rules = new List<KVCompiledValidationRule>();

        foreach (var factory in _validationFactories)
        {
            rules.AddRange(factory.Build());
        }

        rules.AddRange(_globalRules.BuildGlobalRules());
        ValidationRules = rules;
    }

}
