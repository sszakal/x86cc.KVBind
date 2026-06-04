using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace x86cc.KVBind.Core;

public sealed class KVBindBuilder<TEntity>
    where TEntity : KVNode
{
    private readonly KVNodeDefinition _definition;

    public KVBindBuilder()
        : this(
            string.Empty,
            _ => throw new NotSupportedException("Root node definition cannot be resolved from a parent."))
    {
    }

    private KVBindBuilder(string subSegmentPath, Func<KVNode, KVNode> getNode)
    {
        _definition = new KVNodeDefinition
        {
            SubSegmentPath = subSegmentPath,
            GetChildNode = getNode
        };
    }

    private bool _isBuilt;

    // Forward reference to the definition being built — use for self-referential or
    // mutually-recursive nested node declarations. Call Build() after all declarations
    // are complete; SelfReference exposes the same object without locking the builder.
    public KVNodeDefinition SelfReference => _definition;

    public KVNodeDefinition Build()
    {
        if (_isBuilt)
        {
            return _definition;
        }

        _isBuilt = true;
        return _definition;
    }

    public void Field<TValue>(Expression<Func<TEntity, TValue>> selector)
    {
        Field(selector, configure: null);
    }

    public void Field<TValue>(Expression<Func<TEntity, TValue>> selector, Action<KVFieldOptionsBuilder<TValue>>? configure)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (typeof(KVNestedNode).IsAssignableFrom(typeof(TValue)))
        {
            throw new InvalidOperationException($"Nested node type '{typeof(TValue).FullName}' cannot be declared as a field. Use NestedNode(...).");
        }

        var propertyName = ResolveSelectorKey(selector);
        var options = new KVFieldOptionsBuilder<TValue>();
        configure?.Invoke(options);
        var validationRules = options.BuildValidationRules();

        _definition.Fields.RemoveAll(field => string.Equals(field.SubSegmentPath, propertyName, StringComparison.Ordinal));
        var fieldDefinition = new KVFieldDefinition
        {
            SubSegmentPath = propertyName,
            IsRequired = options.IsRequired,
            AllowedValues = options.AllowedValuesDefinition
        };
        fieldDefinition.ValidationRules.AddRange(validationRules);

        _definition.Fields.Add(fieldDefinition);
    }

    public void FieldGroup<TGroup>(Expression<Func<TEntity, TGroup>> selector, Action<KVFieldGroupOptionsBuilder>? configure = null)
        where TGroup : KVFieldGroupNode
    {
        AddFieldGroup(selector, define: null, configure);
    }

    public void FieldGroup<TGroup>(
        Expression<Func<TEntity, TGroup>> selector,
        Action<KVBindBuilder<TGroup>> define,
        Action<KVFieldGroupOptionsBuilder>? configure = null)
        where TGroup : KVFieldGroupNode
    {
        ArgumentNullException.ThrowIfNull(define);
        AddFieldGroup(selector, define, configure);
    }

    private void AddFieldGroup<TGroup>(
        Expression<Func<TEntity, TGroup>> selector,
        Action<KVBindBuilder<TGroup>>? define,
        Action<KVFieldGroupOptionsBuilder>? configure)
        where TGroup : KVFieldGroupNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (typeof(KVRootNode).IsAssignableFrom(typeof(TGroup)))
        {
            throw new InvalidOperationException($"Field group type '{typeof(TGroup).FullName}' cannot inherit KVRootNode.");
        }

        if (typeof(KVNestedNode).IsAssignableFrom(typeof(TGroup)))
        {
            throw new InvalidOperationException($"Nested node type '{typeof(TGroup).FullName}' cannot be declared as a field group. Use NestedNode(...).");
        }

        var propertyName = ResolveSelectorKey(selector);
        var getter = selector.Compile();
        var options = new KVFieldGroupOptionsBuilder();
        configure?.Invoke(options);

        _definition.Nodes.RemoveAll(node => string.Equals(node.SubSegmentPath, propertyName, StringComparison.Ordinal));
        var childBuilder = new KVBindBuilder<TGroup>(
            propertyName,
            owner => getter((TEntity)owner));
        define?.Invoke(childBuilder);
        var nodeDefinition = childBuilder.Build();

        foreach (var tag in options.Tags)
        {
            nodeDefinition.Tags.Add(tag);
        }

        nodeDefinition.IsResettable = options.IsResettable;
        _definition.Nodes.Add(nodeDefinition);
    }

    public void Collection<TModel>(
        Expression<Func<TEntity, KVCollectionNode<TModel>>> selector,
        Action<KVCollectionOptionsBuilder<TEntity, TModel>>? configure = null)
        where TModel : KVCollectionItemNode, new()
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (typeof(KVRootNode).IsAssignableFrom(typeof(TModel)))
        {
            throw new InvalidOperationException($"Collection item base type '{typeof(TModel).FullName}' cannot inherit KVRootNode.");
        }

        var propertyName = ResolveSelectorKey(selector);
        var getter = selector.Compile();
        var options = new KVCollectionOptionsBuilder<TEntity, TModel>(ResolveSelectorKey);
        configure?.Invoke(options);
        options.BuildValidationRules();

        if (options.ItemDefinitions.Count == 0)
        {
            throw new InvalidOperationException($"Collection '{propertyName}' must declare at least one item type with Item<TItem>(...).");
        }

        _definition.Collections.RemoveAll(collection => string.Equals(collection.SubSegmentPath, propertyName, StringComparison.Ordinal));

        var collectionDefinition = new KVCollectionDefinition
        {
            SubSegmentPath = propertyName,
            GetCollection = owner => getter((TEntity)owner)
        };

        foreach (var itemDefinition in options.ItemDefinitions)
        {
            collectionDefinition.AddItemDefinition(itemDefinition.ModelType, itemDefinition.TypeToken, itemDefinition.NodeDefinition);
        }

        collectionDefinition.NotEmpty = options.NotEmptyRule;
        collectionDefinition.MinCount = options.MinCountValue;
        collectionDefinition.MaxCount = options.MaxCountValue;
        collectionDefinition.AggregateRules.AddRange(options.AggregateRules);
        collectionDefinition.ValidationRules.AddRange(options.ValidationRules);
        foreach (var operation in options.PatchOperations)
        {
            collectionDefinition.AddPatchOperation(operation);
        }

        _definition.Collections.Add(collectionDefinition);
    }

    public void NestedNode<TBase>(
        Expression<Func<TEntity, TBase?>> selector,
        Action<KVNestedNodeOptionsBuilder<TBase>> configure)
        where TBase : KVNestedNode
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(configure);

        if (typeof(KVRootNode).IsAssignableFrom(typeof(TBase)))
        {
            throw new InvalidOperationException($"Nested node base type '{typeof(TBase).FullName}' cannot inherit KVRootNode.");
        }

        var propertyName = ResolveSelectorKey(selector);
        var options = new KVNestedNodeOptionsBuilder<TBase>();
        configure(options);

        if (options.TypeDefinitions.Count == 0)
        {
            throw new InvalidOperationException($"Nested node '{propertyName}' must declare at least one subtype with Bind<TSubtype>(...).");
        }

        _definition.NestedNodes.RemoveAll(nestedNode => string.Equals(nestedNode.SubSegmentPath, propertyName, StringComparison.Ordinal));

        var nestedNodeDefinition = new KVNestedNodeDefinition
        {
            SubSegmentPath = propertyName
        };

        foreach (var typeDefinition in options.TypeDefinitions)
        {
            nestedNodeDefinition.AddTypeDefinition(typeDefinition.ModelType, typeDefinition.TypeToken, typeDefinition.NodeDefinition);
        }

        _definition.NestedNodes.Add(nestedNodeDefinition);
    }

    public void Validation(Action<KVGroupRuleBuilder<TEntity>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var scopeKeys = ResolveValidationScopeKeys();
        var builder = new KVGroupRuleBuilder<TEntity>(ResolveSelectorKey);
        configure(builder);
        var rules = builder.BuildGlobalRules();
        RegisterValidationRules(scopeKeys, rules);
    }

    public void Validation(Action<KVGroupValidationProfileBuilder<TEntity>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var scopeKeys = ResolveValidationScopeKeys();
        var builder = new KVGroupValidationProfileBuilder<TEntity>(ResolveSelectorKey);
        configure(builder);
        var rules = builder.Build();
        RegisterValidationRules(scopeKeys, rules);
    }

    public void OnChange(
        Func<KVChangePathBuilder<TEntity>, KVChangePath> path,
        Expression<Func<TEntity, Action<KVChangeContext<TEntity>>>> action)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(action);

        var builder = new KVChangePathBuilder<TEntity>(ResolveSelectorKey);
        var changePath = path(builder);
        _definition.ChangeReactions.Add(KVChangeReactionDescriptor.Create(changePath, action));
    }

    private void RegisterValidationRules(IReadOnlyList<string> scopeKeys, IReadOnlyList<KVCompiledValidationRule> rules)
    {
        if (rules.Count == 0)
        {
            return;
        }

        foreach (var scope in scopeKeys)
        {
            _definition.ValidationRegistrations.Add(new KVValidationRegistration(scope, false, rules));
        }
    }

    private IReadOnlyList<string> ResolveValidationScopeKeys()
    {
        var scopeKeys = _definition.Fields.ConvertAll(field => field.SubSegmentPath);
        scopeKeys.AddRange(_definition.Nodes.ConvertAll(node => node.SubSegmentPath));
        scopeKeys.AddRange(_definition.Collections.ConvertAll(collection => collection.SubSegmentPath));
        scopeKeys.AddRange(_definition.NestedNodes.ConvertAll(nestedNode => nestedNode.SubSegmentPath));

        var distinct = new HashSet<string>(scopeKeys, StringComparer.Ordinal);
        if (distinct.Count == 0)
        {
            throw new InvalidOperationException("Validation requires at least one field or collection declared in the same definition.");
        }

        return [.. distinct];
    }

    private static string ResolveSelectorKey(LambdaExpression selector)
    {
        var body = selector.Body;
        if (body is UnaryExpression unaryExpression
            && (unaryExpression.NodeType == ExpressionType.Convert || unaryExpression.NodeType == ExpressionType.ConvertChecked))
        {
            body = unaryExpression.Operand;
        }

        if (body is not MemberExpression memberExpression)
        {
            throw new ArgumentException("Selector must target a property.", nameof(selector));
        }

        // Prefer the canonical key declared in [KVBind("key")] over the C# property name.
        // This is the same key the source generator uses for GetField/SetField calls.
        var kvBind = memberExpression.Member.GetCustomAttribute<KVBindAttribute>();
        return kvBind?.CanonicalKey ?? memberExpression.Member.Name;
    }

}
