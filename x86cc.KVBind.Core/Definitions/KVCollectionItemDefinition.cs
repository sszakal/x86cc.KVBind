using System;

namespace x86cc.KVBind.Core;

public sealed class KVCollectionItemDefinition
{
    public required Type ModelType { get; init; }

    public required string TypeToken { get; init; }

    public required KVNodeDefinition NodeDefinition { get; init; }
}

// A predefined collection item: ApplyDefaults creates it under ItemId (its canonical path segment) when missing.
public sealed class KVCollectionSeed
{
    internal KVCollectionSeed(string itemId, KVCollectionItemDefinition itemDefinition, Action<KVCollectionItemNode>? configure)
    {
        this.ItemId = itemId;
        this.ItemDefinition = itemDefinition;
        this.Configure = configure;
    }

    public string ItemId { get; }

    public KVCollectionItemDefinition ItemDefinition { get; }

    internal Action<KVCollectionItemNode>? Configure { get; }
}
