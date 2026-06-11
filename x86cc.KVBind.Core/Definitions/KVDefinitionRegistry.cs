using System;
using System.Collections.Concurrent;
using System.Reflection;

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

// Lets a model declare its own definition inline via a static abstract member, so it can be resolved
// without an explicit IKVModelDefinitionBuilder registration.
public interface IKVNodeDefinition
{
    static abstract KVNodeDefinition Definition { get; }
}

public class KVDefinitionRegistry: IKVDefinitionRegistry
{
    private static readonly MethodInfo StaticDefinitionOfMethod =
        typeof(KVDefinitionRegistry).GetMethod(nameof(StaticDefinitionOf), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly ConcurrentDictionary<Type, KVNodeDefinition> _definitions = new();

    public KVDefinitionRegistry(IKVModelDefinitionBuilder[] kvModelDefinitionBuilders)
    {
        ArgumentNullException.ThrowIfNull(kvModelDefinitionBuilders);

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
        // Explicit registrations win; otherwise fall back to a model that carries its own definition
        // via IKVNodeDefinition, caching the resolved definition for subsequent lookups.
        if (_definitions.TryGetValue(typeof(TModel), out var definition))
            return definition;

        if (typeof(TModel).IsAssignableTo(typeof(IKVNodeDefinition)))
            return _definitions.GetOrAdd(typeof(TModel), ResolveStaticDefinition);

        throw new InvalidOperationException($"KV definition for model type '{typeof(TModel).FullName}' is not registered.");
    }

    // The static abstract member can only be reached through a type parameter constrained to the
    // interface, so Get<TModel> (constrained only to KVRootNode) invokes the generic helper reflectively.
    private static KVNodeDefinition ResolveStaticDefinition(Type modelType) =>
        (KVNodeDefinition)StaticDefinitionOfMethod.MakeGenericMethod(modelType).Invoke(null, null)!;

    private static KVNodeDefinition StaticDefinitionOf<T>()
        where T : KVRootNode, IKVNodeDefinition
        => T.Definition ?? throw new InvalidOperationException(
            $"'{typeof(T).FullName}' implements IKVNodeDefinition but its static Definition is null.");
}
