using System;

namespace x86cc.KVBind.Core;

public sealed class KVCollectionItemDefinition
{
    public required Type ModelType { get; init; }

    public required string TypeToken { get; init; }

    public required KVNodeDefinition NodeDefinition { get; init; }
}