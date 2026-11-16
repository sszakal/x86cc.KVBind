---
title: Reading definitions
sidebar_position: 1
---

# Reading definitions

A built `KVNodeDefinition` is a complete, read-only description of a model: its members, types, rules, defaults, allowed values, operations and flags. Generic code — a form renderer, an export, an admin tool, a test generator — can walk it instead of hard-coding a model.

For a client in another process (a browser, a mobile app), use the [schema package](/schema), which turns the same information into plain serializable classes. Read definitions directly when the code runs in .NET next to KVBind.

## The definition tree

| Type | Describes | Children |
| --- | --- | --- |
| `KVNodeDefinition` | the root, a field group, a collection item type, a nested subtype | `Fields`, `Nodes` (field groups), `Collections`, `NestedNodes` |
| `KVFieldDefinition` | a field | — |
| `KVCollectionDefinition` | a collection | `ItemDefinitionsByToken` → `KVCollectionItemDefinition.NodeDefinition` |
| `KVNestedNodeDefinition` | a nested node slot | `TypeDefinitionsByToken` → `KVNestedNodeTypeDefinition.NodeDefinition` |

All of them derive from `KVDefinition`, which has `SubSegmentPath` (the member's key), `DisplayName`, `Annotations` and `IsPatchReadOnly`. The lists keep declaration order.

## Finding a member

`FindField`, `FindNode`, `FindCollection` and `FindNestedNode` look up a **direct** member by its key; walk the tree one key at a time:

```csharp
var code = definition.FindNode("General")?.FindField("Code");

var amount = definition.FindCollection("LineItems")?
    .GetItemDefinition(typeof(AgreementLineItem))   // or GetItemDefinition("Line") by token
    .NodeDefinition.FindField("Amount");

var companyName = definition.FindNestedNode("Party")?
    .GetTypeDefinition("COMPANY")                    // or GetTypeDefinition(typeof(CompanyParty))
    .NodeDefinition.FindField("CompanyName");
```

`definition.FindField("General/Code")` returns `null`: keys, not paths.

## What each definition exposes

**Fields** (`KVFieldDefinition`)

| Member | |
| --- | --- |
| `ValueType` | the property's .NET type |
| `IsRequired`, `MinLength`, `MaxLength`, `Pattern`, `Min`, `Max` | always-applied [value rules](/behavior/validation#value-rules) |
| `HasDefault`, `DefaultValue` | the declared default |
| `HasAllowedValues`, `AllowedValueOptions`, `AllowedValuesApplyToElements` | [allowed values](/dsl/allowed-values) |
| `IsInherited` | read from the parent in a [child aggregate](/workflows/inheritance) |

**Collections** (`KVCollectionDefinition`)

| Member | |
| --- | --- |
| `ItemDefinitionsByToken`, `ItemDefinitionsByType`, `GetItemDefinition(...)` | item types |
| `NotEmpty`, `MinCount`, `MaxCount`, `AggregateRules` | always-applied collection rules |
| `Seeds` | [predefined items](/dsl/collections#predefined-items-seeds) |
| `AllowedOperations`, `IsOperationAllowed(op)` | [structural patch restrictions](/dsl/collections#restricting-structural-patch-operations) (`null` = unrestricted) |
| `OperationNames` | custom collection operations |
| `IsInherited` | |

**Nested nodes** (`KVNestedNodeDefinition`)

| Member | |
| --- | --- |
| `TypeDefinitionsByToken`, `TypeDefinitionsByType`, `GetTypeDefinition(...)` | bound subtypes |
| `DefaultTypeToken`, `IsRequired`, `IsInherited` | |

**Nodes** (`KVNodeDefinition`)

| Member | |
| --- | --- |
| `OperationNames` | [custom operations](/patching/custom-operations) the node accepts |
| `IsResettable` | a field group declared `Resettable()` |
| `CurrentSchemaVersion`, `Migrations` | on the root: the [schema version](/workflows/migrations) |
| `InheritedPrefixes` | on the root: the paths a child inherits |

## Example: listing every field

```csharp
static IEnumerable<string> FieldPaths(KVNodeDefinition node, string prefix = "")
{
    foreach (var field in node.Fields)
        yield return prefix + field.SubSegmentPath;

    foreach (var group in node.Nodes)
        foreach (var path in FieldPaths(group, $"{prefix}{group.SubSegmentPath}/"))
            yield return path;

    foreach (var collection in node.Collections)
        foreach (var item in collection.ItemDefinitionsByToken.Values)
            foreach (var path in FieldPaths(item.NodeDefinition, $"{prefix}{collection.SubSegmentPath}/<id>/"))
                yield return path;

    foreach (var nested in node.NestedNodes)
        foreach (var type in nested.TypeDefinitionsByToken.Values)
            foreach (var path in FieldPaths(type.NodeDefinition, $"{prefix}{nested.SubSegmentPath}/"))
                yield return path;
}
```

Definitions can be [recursive](/dsl/nested-nodes#recursive-models): a walker over such a model must remember the definitions it has visited, or it never ends. The schema package solves this with `Id`/`Ref` pairs.

## Merging overlays directly

`KVOverlay.BeginRebase` is built on `KVMerge.Merge(baseSnapshot, theirChanges, ourChanges)`, a three-way merge of two change dictionaries over a common base. It returns a `KVMergeResult` with the `Conflicts` and the merged collection membership. Use it only for custom workflows outside the [rebase flow](/workflows/drafts-and-rebase); the overlay API covers the common case.
