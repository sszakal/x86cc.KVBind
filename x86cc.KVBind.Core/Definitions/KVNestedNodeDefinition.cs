using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVNestedNodeDefinition : KVDefinition
{
    private readonly Dictionary<Type, KVNestedNodeTypeDefinition> _typeDefinitionsByType = new();
    private readonly Dictionary<string, KVNestedNodeTypeDefinition> _typeDefinitionsByToken = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<Type, KVNestedNodeTypeDefinition> TypeDefinitionsByType => _typeDefinitionsByType;

    public IReadOnlyDictionary<string, KVNestedNodeTypeDefinition> TypeDefinitionsByToken => _typeDefinitionsByToken;

    internal void AddTypeDefinition(Type modelType, string typeToken, KVNodeDefinition nodeDefinition)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(nodeDefinition);

        if (_typeDefinitionsByType.ContainsKey(modelType))
        {
            throw new InvalidOperationException($"Nested node type '{modelType.FullName}' is already declared.");
        }

        if (_typeDefinitionsByToken.ContainsKey(typeToken))
        {
            throw new InvalidOperationException($"Nested node token '{typeToken}' is already declared.");
        }

        var typeDefinition = new KVNestedNodeTypeDefinition
        {
            ModelType = modelType,
            TypeToken = typeToken,
            NodeDefinition = nodeDefinition
        };

        _typeDefinitionsByType[modelType] = typeDefinition;
        _typeDefinitionsByToken[typeToken] = typeDefinition;
    }

    public KVNestedNodeTypeDefinition GetTypeDefinition(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        if (_typeDefinitionsByType.TryGetValue(modelType, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Nested node type '{modelType.FullName}' is not declared for nested node '{SubSegmentPath}'.");
    }

    public KVNestedNodeTypeDefinition GetTypeDefinition(string typeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);

        if (_typeDefinitionsByToken.TryGetValue(typeToken, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Nested node token '{typeToken}' is not declared for nested node '{SubSegmentPath}'.");
    }
}
