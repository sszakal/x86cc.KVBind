using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.Core;

public sealed class KVCollectionOptionsBuilder<TParent, TModel>
    where TParent : KVNode
    where TModel : KVCollectionItemNode, new()
{
    private readonly KVCollectionRuleBuilder<TModel> globalRules = new();

    private readonly List<IKVCollectionValidationRuleFactory> validationFactories = [];

    private readonly List<KVCollectionItemDefinition> itemDefinitions = [];

    private readonly List<KVPatchOperationDescriptor> patchOperations = [];

    private readonly Dictionary<string, object?> annotations = new(StringComparer.Ordinal);

    internal KVCollectionOptionsBuilder(Func<LambdaExpression, string> resolveSelectorKey)
    {
    }

    internal IReadOnlyList<KVCollectionItemDefinition> ItemDefinitions => this.itemDefinitions;

    internal string? DisplayNameValue { get; private set; }

    internal IReadOnlyDictionary<string, object?> Annotations => this.annotations;

    internal bool IsInherited { get; private set; }

    internal Action<Abstractions.IKVCollectionNode>? DefaultSeedAction { get; private set; }

    internal bool NotEmptyRule => this.globalRules.NotEmptyRule;

    internal int? MinCountValue => this.globalRules.MinCountValue;

    internal int? MaxCountValue => this.globalRules.MaxCountValue;

    internal IReadOnlyList<KVCollectionAggregateRule> AggregateRules => this.globalRules.AggregateRules;

    internal IReadOnlyList<KVCompiledValidationRule> ValidationRules { get; private set; } = [];

    internal IReadOnlyList<KVPatchOperationDescriptor> PatchOperations => this.patchOperations;

    // Root-only: the whole collection is inherited — read-only and parent-sourced when bound with a parent.
    public KVCollectionOptionsBuilder<TParent, TModel> Inherited()
    {
        this.IsInherited = true;
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        this.DisplayNameValue = displayName;
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Annotate(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.annotations[key] = value;
        return this;
    }

    // Seeds initial items into a fresh, empty collection (ApplyDefaults), e.g. `c => c.Create<TItem>()`.
    public KVCollectionOptionsBuilder<TParent, TModel> Default(Action<KVCollectionNode<TModel>> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        this.DefaultSeedAction = collection => seed((KVCollectionNode<TModel>)collection);
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>(Action<KVBindBuilder<TItem>> configure)
        where TItem : TModel, new()
    {
        return this.Item(typeof(TItem).Name, configure);
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>(string typeToken, Action<KVBindBuilder<TItem>> configure)
        where TItem : TModel, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new KVBindBuilder<TItem>();
        configure(builder);
        return this.RegisterItem<TItem>(typeToken, builder.Build());
    }

    // Self-describing item subtype: the item type carries its own definition via IKVNodeDefinition, so no
    // inline configuration is needed (enforced at compile time).
    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>()
        where TItem : TModel, IKVNodeDefinition, new()
        => this.Item<TItem>(typeof(TItem).Name);

    public KVCollectionOptionsBuilder<TParent, TModel> Item<TItem>(string typeToken)
        where TItem : TModel, IKVNodeDefinition, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        return this.RegisterItem<TItem>(typeToken, TItem.Definition);
    }

    public KVCollectionOptionsBuilder<TParent, TModel> NotEmpty()
    {
        this.globalRules.NotEmpty();
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> MinCount(int minCount)
    {
        this.globalRules.MinCount(minCount);
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> MaxCount(int maxCount)
    {
        this.globalRules.MaxCount(maxCount);
        return this;
    }

    public KVCollectionAggregateRuleBuilder<TModel> AggregateSum(string fieldKey)
    {
        return this.globalRules.AggregateSum(fieldKey);
    }

    public KVCollectionAggregateRuleBuilder<TModel> AggregateSum<TItem, TValue>(Expression<Func<TItem, TValue>> selector)
        where TItem : KVCollectionItemNode
    {
        return this.globalRules.AggregateSum(selector);
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Validation(Action<IkvCollectionValidationProfileBuilder<TModel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new IkvCollectionValidationProfileBuilder<TModel>();
        configure(builder);
        this.validationFactories.Add(builder);
        return this;
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Operation<TArgument>(Expression<Func<TParent, Action<TArgument>>> methodSelector)
    {
        var operation = KVPatchOperationDescriptor.GetMethodName(methodSelector).ToUpperInvariant();
        return this.Operation(operation, methodSelector);
    }

    public KVCollectionOptionsBuilder<TParent, TModel> Operation<TArgument>(string operation, Expression<Func<TParent, Action<TArgument>>> methodSelector)
    {
        var patchOperation = KVPatchOperationDescriptor.CustomCollection(operation, methodSelector);
        if (this.patchOperations.Exists(existing => string.Equals(existing.Operation, patchOperation.Operation, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Patch operation '{patchOperation.Operation}' is already registered for this collection.");
        }

        this.patchOperations.Add(patchOperation);
        return this;
    }

    internal void BuildValidationRules()
    {
        var rules = new List<KVCompiledValidationRule>();

        foreach (var factory in this.validationFactories)
        {
            rules.AddRange(factory.Build());
        }

        // Collection-level NotEmpty/MinCount/MaxCount/AggregateSum are copied onto the definition and
        // evaluated by KVValidationRuntime, so they must not also be compiled into ValidationRules.
        this.ValidationRules = rules;
    }

    private KVCollectionOptionsBuilder<TParent, TModel> RegisterItem<TItem>(string typeToken, KVNodeDefinition nodeDefinition)
        where TItem : TModel, new()
    {
        if (typeof(KVRootNode).IsAssignableFrom(typeof(TItem)))
        {
            throw new InvalidOperationException($"Collection item type '{typeof(TItem).FullName}' cannot inherit KVRootNode.");
        }

        if (this.itemDefinitions.Exists(definition => definition.ModelType == typeof(TItem)))
        {
            throw new InvalidOperationException($"Collection item type '{typeof(TItem).FullName}' is already declared.");
        }

        if (this.itemDefinitions.Exists(definition => string.Equals(definition.TypeToken, typeToken, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Collection item token '{typeToken}' is already declared.");
        }

        this.itemDefinitions.Add(new KVCollectionItemDefinition
        {
            ModelType = typeof(TItem),
            TypeToken = typeToken,
            NodeDefinition = nodeDefinition
        });

        return this;
    }
}
