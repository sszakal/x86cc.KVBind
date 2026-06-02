using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Definitions;

public interface IKVDefinitionRegistry
{
    KVNodeDefinition Get<TModel>() where TModel : KVRootNode;
}

public interface IKVModelDefinitionBuilder
{
    Type ModelType { get; }
    KVNodeDefinition Build();
}

public class KVDefinitionRegistry: IKVDefinitionRegistry
{
    private readonly Dictionary<Type, KVNodeDefinition> _definitions;
    
    public KVDefinitionRegistry(IKVModelDefinitionBuilder[] kvModelDefinitionBuilders)
    {
        ArgumentNullException.ThrowIfNull(kvModelDefinitionBuilders);

        _definitions = new Dictionary<Type, KVNodeDefinition>();
        foreach (var builder in kvModelDefinitionBuilders)
        {
            if (!typeof(KVRootNode).IsAssignableFrom(builder.ModelType))
            {
                throw new InvalidOperationException($"KV root definition type '{builder.ModelType.FullName}' must inherit KVRootNode.");
            }

            if (!_definitions.TryAdd(builder.ModelType, builder.Build()))
            {
                throw new InvalidOperationException($"KV definition for model type '{builder.ModelType.FullName}' is already registered.");
            }
        }
    }

    public KVNodeDefinition Get<TModel>() where TModel : KVRootNode
    {
        return _definitions.TryGetValue(typeof(TModel), out var definition)
            ? definition
            : throw new InvalidOperationException($"KV definition for model type '{typeof(TModel).FullName}' is not registered.");
    }
}
