using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Abstractions;

namespace x86cc.KVBind.Core;

public class KVCollectionDefinition : KVDefinition
{
    private readonly Dictionary<Type, KVCollectionItemDefinition> itemDefinitionsByType = new();

    private readonly Dictionary<string, KVCollectionItemDefinition> itemDefinitionsByToken = new(StringComparer.Ordinal);

    private readonly Dictionary<Type, string> typeToToken = new();

    private readonly Dictionary<string, Type> tokenToType = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<Type, KVCollectionItemDefinition> ItemDefinitionsByType => this.itemDefinitionsByType;

    public IReadOnlyDictionary<string, KVCollectionItemDefinition> ItemDefinitionsByToken => this.itemDefinitionsByToken;

    public IReadOnlyDictionary<Type, string> TypeToName => this.typeToToken;

    public IReadOnlyDictionary<string, Type> NameToType => this.tokenToType;

    // Root-only marker: the whole collection is inherited (read-only, parent-sourced) when bound with a
    // parent snapshot. See KVOverlay inheritance.
    public bool IsInherited { get; set; }

    public bool NotEmpty { get; set; }

    public int? MinCount { get; set; }

    public int? MaxCount { get; set; }

    public List<KVCollectionAggregateRule> AggregateRules { get; } = new();

    public List<KVCompiledValidationRule> ValidationRules { get; } = new();

    // Predefined items with stable ids, in declaration order.
    public IReadOnlyList<KVCollectionSeed> Seeds { get; internal set; } = [];

    // Structural patch operations (ADD/REMOVE/MOVE/custom) clients may apply. Null = unrestricted.
    public IReadOnlySet<string>? AllowedOperations { get; internal set; }

    public Func<KVNode, IKVCollectionNode> GetCollection { get; init; } = _ => throw new NotImplementedException();

    // Seeds initial items into a fresh, empty collection (ApplyDefaults). Null = default to empty.
    internal Action<IKVCollectionNode>? DefaultSeed { get; set; }

    internal Dictionary<string, KVPatchOperationDescriptor> PatchOperations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOperationAllowed(string operation)
        => this.AllowedOperations is null || this.AllowedOperations.Contains(operation);

    public KVCollectionItemDefinition GetItemDefinition(Type modelType)
    {
        ArgumentNullException.ThrowIfNull(modelType);

        if (this.itemDefinitionsByType.TryGetValue(modelType, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Collection item type '{modelType.FullName}' is not declared for collection '{this.SubSegmentPath}'.");
    }

    public KVCollectionItemDefinition GetItemDefinition(string typeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);

        if (this.itemDefinitionsByToken.TryGetValue(typeToken, out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"Collection item token '{typeToken}' is not declared for collection '{this.SubSegmentPath}'.");
    }

    internal void AddPatchOperation(KVPatchOperationDescriptor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!this.PatchOperations.TryAdd(operation.Operation, operation))
        {
            throw new InvalidOperationException($"Patch operation '{operation.Operation}' is already registered for collection '{this.SubSegmentPath}'.");
        }
    }

    internal void AddItemDefinition(Type modelType, string typeToken, KVNodeDefinition nodeDefinition)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        ArgumentNullException.ThrowIfNull(nodeDefinition);

        if (this.itemDefinitionsByType.ContainsKey(modelType))
        {
            throw new InvalidOperationException($"Collection item type '{modelType.FullName}' is already declared.");
        }

        if (this.itemDefinitionsByToken.ContainsKey(typeToken))
        {
            throw new InvalidOperationException($"Collection item token '{typeToken}' is already declared.");
        }

        var itemDefinition = new KVCollectionItemDefinition
        {
            ModelType = modelType,
            TypeToken = typeToken,
            NodeDefinition = nodeDefinition
        };

        this.itemDefinitionsByType[modelType] = itemDefinition;
        this.itemDefinitionsByToken[typeToken] = itemDefinition;
        this.typeToToken[modelType] = typeToken;
        this.tokenToType[typeToken] = modelType;
    }
}
