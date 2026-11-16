---
title: Source generation
sidebar_position: 2
---

# Source generation

`x86cc.KVBind.SourceGenerator` is a Roslyn incremental generator. It implements `partial` properties marked with `[KVBind("Key")]` so that they read and write through the bound KVBind runtime.

```csharp
public partial class Agreement : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }
}
```

The DSL defines schema and behavior; the generator keeps the model surface ergonomic. The attribute only names the canonical key — a property is part of the schema only once a `KVBindBuilder<T>` declares it.

## Which properties are `partial`

| Member | Declaration |
| --- | --- |
| Scalar field | `public partial T Name { get; set; }` |
| Nested node | `public partial TBase? Name { get; private set; }` (or a public setter, which warns) |
| Field group | `public TGroup Name { get; } = new();` — not partial, no setter |
| Collection | `public KVCollectionNode<TItem> Name { get; } = new();` — not partial, no setter |

## Keys

- Canonical keys may only contain `A-Z`, `a-z`, `0-9` and `_`.
- Keys are case-sensitive everywhere: generator, definitions, storage and patch paths.
- A key must be unique within a type, across all of its partial declarations.

## Diagnostics

| Id | Severity | Meaning |
| --- | --- | --- |
| `KVB001` | Error | A scalar or nested-node `[KVBind]` property is not `partial`. |
| `KVB002` | Error | The `[KVBind]` key is missing. |
| `KVB003` | Error | Duplicate key (checked across all partial declarations of a type). |
| `KVB004` | Error | The key contains characters other than `A-Z`, `a-z`, `0-9`, `_`. |
| `KVB005` | Warning | `[KVBind]` is used on a type that is not a `KVNode`. |
| `KVB006` | Error | A field-group or collection property has a setter. |
| `KVB007` | Error | A field-group or collection property is `partial`. |
| `KVB008` | Warning | A nested-node property has a public setter. |
| `KVB009` | Error | The model is declared inside a containing type that is not `partial`. |
