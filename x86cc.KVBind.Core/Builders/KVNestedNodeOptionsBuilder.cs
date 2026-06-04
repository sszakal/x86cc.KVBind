using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVNestedNodeOptionsBuilder<TBase>
    where TBase : KVNestedNode
{
    private readonly List<KVNestedNodeTypeDefinition> _typeDefinitions = [];

    internal IReadOnlyList<KVNestedNodeTypeDefinition> TypeDefinitions => _typeDefinitions;

    public KVNestedNodeOptionsBuilder<TBase> Bind<TSubtype>(Action<KVBindBuilder<TSubtype>> configure)
        where TSubtype : TBase, new()
    {
        return Bind(typeof(TSubtype).Name, configure);
    }

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
