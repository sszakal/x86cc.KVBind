using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVNestedNodeDefinition : KVDefinition
{
    private readonly Dictionary<Type, KVNestedNodeTypeDefinition> typeDefinitionsByType = new();

    private readonly Dictionary<string, KVNestedNodeTypeDefinition> typeDefinitionsByToken = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<Type, KVNestedNodeTypeDefinition> TypeDefinitionsByType => this.typeDefinitionsByType;

    public IReadOnlyDictionary<string, KVNestedNodeTypeDefinition> TypeDefinitionsByToken => this.typeDefinitionsByToken;

    // The subtype token a fresh aggregate initializes this slot to (ApplyDefaults). Null = leave unset.
    public string? DefaultTypeToken { get; set; }

    // Root-only marker: the whole nested node is inherited (read-only, parent-sourced) when bound with a
    // parent snapshot. See KVOverlay inheritance.
    public bool IsInherited { get; set; }

    public KVNestedNodeTypeDefinition GetTypeDefinition(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        if (this.typeDefinitionsByType.TryGetValue(modelType, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Nested node type '{modelType.FullName}' is not declared for nested node '{this.SubSegmentPath}'.");
    }

    public KVNestedNodeTypeDefinition GetTypeDefinition(string typeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);

        if (this.typeDefinitionsByToken.TryGetValue(typeToken, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Nested node token '{typeToken}' is not declared for nested node '{this.SubSegmentPath}'.");
    }

    internal void AddTypeDefinition(Type modelType, string typeToken, KVNodeDefinition nodeDefinition)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(nodeDefinition);

        if (this.typeDefinitionsByType.ContainsKey(modelType))
        {
            throw new InvalidOperationException($"Nested node type '{modelType.FullName}' is already declared.");
        }

        if (this.typeDefinitionsByToken.ContainsKey(typeToken))
        {
            throw new InvalidOperationException($"Nested node token '{typeToken}' is already declared.");
        }

        var typeDefinition = new KVNestedNodeTypeDefinition
        {
            ModelType = modelType,
            TypeToken = typeToken,
            NodeDefinition = nodeDefinition
        };

        this.typeDefinitionsByType[modelType] = typeDefinition;
        this.typeDefinitionsByToken[typeToken] = typeDefinition;
    }
}
