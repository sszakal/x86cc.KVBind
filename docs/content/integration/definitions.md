---
title: Registering definitions
sidebar_position: 1
---

# Registering definitions

Building a definition validates the whole schema and compiles rules, so build each one **once** at startup and share it. `KVDefinitionRegistry` holds the definitions of an application's root models and hands them out by model type.

## One builder class per model

Implement `IKVModelDefinitionBuilder` for each root model:

```csharp
public sealed class AgreementDefinitionBuilder : IKVModelDefinitionBuilder
{
    public Type ModelType => typeof(Agreement);

    public KVNodeDefinition Build()
    {
        var builder = new KVBindBuilder<Agreement>();
        builder.Field(x => x.Title, f => f.Required().MaxLength(100));
        // ...
        return builder.Build();
    }
}
```

Then create the registry from all builders. It builds every definition immediately and throws when a model type is registered twice or does not derive from `KVRootNode`:

```csharp
var registry = new KVDefinitionRegistry([
    new AgreementDefinitionBuilder(),
    new InvoiceDefinitionBuilder(),
]);

KVNodeDefinition definition = registry.Get<Agreement>();
```

## Definitions declared on the model

Alternatively a model can carry its own definition by implementing `IKVNodeDefinition`, a static abstract member. The registry falls back to it for models that have no registered builder:

```csharp
public partial class Note : KVRootNode, IKVNodeDefinition
{
    [KVBind("Text")]
    public partial string? Text { get; set; }

    public static KVNodeDefinition Definition { get; } = Build();

    private static KVNodeDefinition Build()
    {
        var builder = new KVBindBuilder<Note>();
        builder.Field(x => x.Text);
        return builder.Build();
    }
}

var definition = registry.Get<Note>();   // resolved from Note.Definition and cached
```

Explicit registrations win over `IKVNodeDefinition`.

## Dependency injection

Register the registry as a singleton and depend on `IKVDefinitionRegistry`:

```csharp
builder.Services.AddSingleton<IKVDefinitionRegistry>(_ => new KVDefinitionRegistry([
    new AgreementDefinitionBuilder(),
]));
```

```csharp
public sealed class AgreementService(IKVDefinitionRegistry registry)
{
    private readonly KVNodeDefinition definition = registry.Get<Agreement>();
}
```

The [schema package](/schema) reads from the same registry: `KVSchemaMapper.Map<Agreement>(registry)`.

## Namespaces

| Namespace | Contains |
| --- | --- |
| `x86cc.KVBind.Core` | node types, `[KVBind]`, `KVBindBuilder<T>`, definitions (`KVNodeDefinition`, …), patching, validation, reactions |
| `x86cc.KVBind.Core.Model` | `KVSnapshot`, `KVOverlay`, `KVCommit`, `KVValue`, `KVDictionary`, rebase types |
| `x86cc.KVBind.Core.Definitions` | `KVDefinitionRegistry`, `IKVDefinitionRegistry`, `IKVModelDefinitionBuilder`, `IKVNodeDefinition` |
| `x86cc.KVBind.Core.Migrations` | migrations, `KVTarget`, `KVMigrator`, `NewSnapshot()` |
| `x86cc.KVBind.Schema` | `KVSchemaMapper` and the schema model |
