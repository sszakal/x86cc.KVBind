using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;

namespace x86cc.KVBind.Core;

public class KVCollectionDefinition : KVDefinition
{
    private readonly Dictionary<Type, KVCollectionItemDefinition> _itemDefinitionsByType = new();
    private readonly Dictionary<string, KVCollectionItemDefinition> _itemDefinitionsByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _typeToToken = new();
    private readonly Dictionary<string, Type> _tokenToType = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<Type, KVCollectionItemDefinition> ItemDefinitionsByType => _itemDefinitionsByType;

    public IReadOnlyDictionary<string, KVCollectionItemDefinition> ItemDefinitionsByToken => _itemDefinitionsByToken;

    public IReadOnlyDictionary<Type, string> TypeToName => _typeToToken;

    public IReadOnlyDictionary<string, Type> NameToType => _tokenToType;
    
    // Root-only marker: the whole collection is inherited (read-only, parent-sourced) when bound with a
    // parent snapshot. See KVOverlay inheritance.
    public bool IsInherited { get; set; }

    public bool NotEmpty { get; set; }

    public int? MinCount { get; set; }

    public int? MaxCount { get; set; }

    // Seeds initial items into a fresh, empty collection (ApplyDefaults). Null = default to empty.
    internal Action<IKVCollectionNode>? DefaultSeed { get; set; }

    public List<KVCollectionAggregateRule> AggregateRules { get; } = new();

    public List<KVCompiledValidationRule> ValidationRules { get; } = new();

    internal Dictionary<string, KVPatchOperationDescriptor> PatchOperations { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal void AddPatchOperation(KVPatchOperationDescriptor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!PatchOperations.TryAdd(operation.Operation, operation))
        {
            throw new InvalidOperationException($"Patch operation '{operation.Operation}' is already registered for collection '{SubSegmentPath}'.");
        }
    }
    
    internal void AddItemDefinition(Type modelType, string typeToken, KVNodeDefinition nodeDefinition)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(nodeDefinition);

        if (_itemDefinitionsByType.ContainsKey(modelType))
        {
            throw new InvalidOperationException($"Collection item type '{modelType.FullName}' is already declared.");
        }

        if (_itemDefinitionsByToken.ContainsKey(typeToken))
        {
            throw new InvalidOperationException($"Collection item token '{typeToken}' is already declared.");
        }

        var itemDefinition = new KVCollectionItemDefinition
        {
            ModelType = modelType,
            TypeToken = typeToken,
            NodeDefinition = nodeDefinition
        };

        _itemDefinitionsByType[modelType] = itemDefinition;
        _itemDefinitionsByToken[typeToken] = itemDefinition;
        _typeToToken[modelType] = typeToken;
        _tokenToType[typeToken] = modelType;
    }

    public KVCollectionItemDefinition GetItemDefinition(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        if (_itemDefinitionsByType.TryGetValue(modelType, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Collection item type '{modelType.FullName}' is not declared for collection '{SubSegmentPath}'.");
    }

    public KVCollectionItemDefinition GetItemDefinition(string typeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);

        if (_itemDefinitionsByToken.TryGetValue(typeToken, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Collection item token '{typeToken}' is not declared for collection '{SubSegmentPath}'.");
    }

    public Func<KVNode, IKVCollectionNode> GetCollection { get; init; } = _ => throw new NotImplementedException();
}
