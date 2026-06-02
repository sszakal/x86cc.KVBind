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
}
