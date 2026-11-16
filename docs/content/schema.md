---
title: Schema package
sidebar_position: 8
---

# Schema package

`x86cc.KVBind.Schema` produces a serializer-neutral description of a model, built from its real `KVNodeDefinition` — so the model's shape is never duplicated by hand. A client can render a form, show labels and allowed values, and hide actions that would be rejected, all from the definition.

```csharp
var schema = KVSchemaMapper.Map<InsuranceClaim>(registry);   // build once, reuse
return Results.Ok(schema);                                   // or MessagePack, XML, ...
```

## What it describes

- structure: fields, groups, collections with their item types, nested nodes with their types
- labels and value kinds
- defaults and always-applied constraints (length, pattern, bounds, required)
- allowed values, including components with placeholders
- read-only, inherited and resettable flags
- collection count and aggregate rules, seeds, allowed and custom operations
- scalar annotations (`Annotate(...)`)

Validation profile and group rules are compiled code and are not described.

## Shape

The model (`KVSchemaDocument`, `KVNodeSchema`, `KVFieldSchema`, …) is made of plain classes: strings, booleans, numbers, enums, lists and nested schema classes, with no serializer attributes and no `object`-typed members. Values such as defaults, bounds and annotations are invariant text, read according to the member's `KVSchemaValueKind`; `KVSchemaValues` holds the conversion rules.

## Recursive definitions

Definitions can be [recursive](/dsl/nested-nodes#recursive-models), so the tree can grow without limit, yet the schema stays finite:

- The first occurrence of a type that occurs inside itself carries an `Id`.
- An occurrence inside itself carries a `Ref` to that id and an empty `Node`.

`KVSchemaReferences.Index(document)` and `KVSchemaReferences.NodeOf(type, index)` resolve them, so a client can walk a path of any depth.
