using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.Core;

public sealed class KVNestedNodeOptionsBuilder<TBase>
    where TBase : KVNestedNode
{
    private readonly List<KVNestedNodeTypeDefinition> _typeDefinitions = [];

    internal IReadOnlyList<KVNestedNodeTypeDefinition> TypeDefinitions => _typeDefinitions;

    internal string? DisplayNameValue { get; private set; }

    private readonly Dictionary<string, object?> _annotations = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, object?> Annotations => _annotations;

    internal bool IsInherited { get; private set; }

    // Root-only: the whole nested node is inherited — read-only and parent-sourced when bound with a parent.
    public KVNestedNodeOptionsBuilder<TBase> Inherited()
    {
        IsInherited = true;
        return this;
    }

    public KVNestedNodeOptionsBuilder<TBase> DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayNameValue = displayName;
        return this;
    }

    public KVNestedNodeOptionsBuilder<TBase> Annotate(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _annotations[key] = value;
        return this;
    }

    internal string? DefaultTypeTokenValue { get; private set; }

    // A fresh aggregate initializes this slot to this subtype (ApplyDefaults). Uses the subtype's default
    // token (typeof(TSubtype).Name) — declare a matching Bind<TSubtype>().
    public KVNestedNodeOptionsBuilder<TBase> DefaultType<TSubtype>() where TSubtype : TBase
    {
        DefaultTypeTokenValue = typeof(TSubtype).Name;
        return this;
    }

    public KVNestedNodeOptionsBuilder<TBase> DefaultType(string typeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        DefaultTypeTokenValue = typeToken;
        return this;
    }

    public KVNestedNodeOptionsBuilder<TBase> Bind<TSubtype>(Action<KVBindBuilder<TSubtype>> configure)
        where TSubtype : TBase, new()
    {
        return Bind(typeof(TSubtype).Name, configure);
    }

    // Self-describing subtype: the subtype carries its own definition via IKVNodeDefinition, so no inline
    // configuration is needed (enforced at compile time).
    public KVNestedNodeOptionsBuilder<TBase> Bind<TSubtype>()
        where TSubtype : TBase, IKVNodeDefinition, new()
        => Bind<TSubtype>(typeof(TSubtype).Name);

    public KVNestedNodeOptionsBuilder<TBase> Bind<TSubtype>(string typeToken)
        where TSubtype : TBase, IKVNodeDefinition, new()
        => Bind<TSubtype>(typeToken, TSubtype.Definition);

    public KVNestedNodeOptionsBuilder<TBase> Bind<TSubtype>(string typeToken, Action<KVBindBuilder<TSubtype>> configure)
        where TSubtype : TBase, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(configure);

        if (typeof(KVRootNode).IsAssignableFrom(typeof(TSubtype)))
        {
            throw new InvalidOperationException($"Nested node type '{typeof(TSubtype).FullName}' cannot inherit KVRootNode.");
        }

        if (_typeDefinitions.Exists(definition => definition.ModelType == typeof(TSubtype)))
        {
            throw new InvalidOperationException($"Nested node type '{typeof(TSubtype).FullName}' is already declared.");
        }

        if (_typeDefinitions.Exists(definition => string.Equals(definition.TypeToken, typeToken, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Nested node token '{typeToken}' is already declared.");
        }

        var builder = new KVBindBuilder<TSubtype>();
        configure(builder);

        _typeDefinitions.Add(new KVNestedNodeTypeDefinition
        {
            ModelType = typeof(TSubtype),
            TypeToken = typeToken,
            NodeDefinition = builder.Build()
        });

        return this;
    }

    // Accepts a pre-built definition — enables recursive/self-referential node graphs.
    // Use the two-pass pattern: build the definition first, then add the recursive nested node
    // referencing that same definition object. Activation is lazy so circular references are safe.
    public KVNestedNodeOptionsBuilder<TBase> Bind<TSubtype>(string typeToken, KVNodeDefinition nodeDefinition)
        where TSubtype : TBase, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(nodeDefinition);

        if (typeof(KVRootNode).IsAssignableFrom(typeof(TSubtype)))
            throw new InvalidOperationException($"Nested node type '{typeof(TSubtype).FullName}' cannot inherit KVRootNode.");

        if (_typeDefinitions.Exists(d => d.ModelType == typeof(TSubtype)))
            throw new InvalidOperationException($"Nested node type '{typeof(TSubtype).FullName}' is already declared.");

        if (_typeDefinitions.Exists(d => string.Equals(d.TypeToken, typeToken, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Nested node token '{typeToken}' is already declared.");

        _typeDefinitions.Add(new KVNestedNodeTypeDefinition
        {
            ModelType = typeof(TSubtype),
            TypeToken = typeToken,
            NodeDefinition = nodeDefinition
        });

        return this;
    }
}
