---
title: Collections
sidebar_position: 2
---

# Collections

A collection is an ordered list of items, each with its own fields, groups, nested nodes and sub-collections.

```csharp
builder.Collection(x => x.LineItems, collection =>
{
    collection.Item<AgreementLineItem>(item =>
    {
        item.Field(x => x.Description);
        item.Field(x => x.Amount);
    });
});
```

A collection needs at least one `Item<TItem>(...)` definition. Item types derive from `KVCollectionItemNode`; the property is a get-only `KVCollectionNode<TItem>`.

## Item ids

Every item has an immutable string id, which is also its canonical path segment (`LineItems/<id>`). Ids may contain `A-Z`, `a-z`, `0-9`, `_` and `-`, and are case-sensitive; GUIDs in `"D"` format qualify. `Create()` without an id generates a GUID — pass your own id when the item should have a stable, meaningful path.

```csharp
var generated = agreement.LineItems.Create();                // LineItems/3f2b…-…
var fromGuid  = agreement.LineItems.Create(Guid.NewGuid());
var named     = agreement.LineItems.Create("shipping");      // LineItems/shipping
```

## Polymorphic collections

Declare several `Item<TSubtype>(...)` definitions to hold different item types. The id and the item type are independent: the id is the path segment, the stored `$type` token selects the subtype. `Get<TSubtype>(id)` returns the item typed (or `null` if absent, and throws if the stored item is a different subtype):

```csharp
var snake  = zoo.Animals.Create<Snake>("Snake");     // Animals/Snake, $type = "Snake"
var python = zoo.Animals.Create<Snake>("Python");    // same type, different id
var lizard = zoo.Animals.Get<Lizard>("Lizard");
```

## Predefined items (seeds)

`Seed<TItem>(...)` declares items every new aggregate starts with, under stable ids, so they are addressable by the same path in every document (for example `/Animals/Snake`). The id defaults to the item's type token:

```csharp
builder.Collection(x => x.Animals, animals => animals
    .Item<Snake>(item => item.Field(x => x.Name, f => f.Default("unnamed")))
    .Item<Lizard>(item => item.Field(x => x.Legs, f => f.Default(4)))
    .Seed<Snake>()                                  // Animals/Snake
    .Seed<Lizard>("Gecko", l => l.Legs = 3)         // Animals/Gecko, overrides the default
    .Seed<Snake>("Python"));                        // Animals/Python
```

- Seeds are created when the collection is born — with a new aggregate, or with the new item that owns the collection — and never afterwards.
- Reopening an aggregate does not re-seed it, and a seeded item that was removed stays removed. Existing aggregates do not pick up seeds added to the schema later.
- Each seeded item first gets its declared defaults, then the optional `configure` callback runs.
- Seed ids must be unique and valid, the seeded type must be declared with `Item<TItem>(...)`, and `Seed` cannot be combined with `Default(...)`.

Declared seeds are available on `KVCollectionDefinition.Seeds`.

## Restricting structural patch operations

`AllowOperations(...)` is an allowlist of the structural patch operations clients may apply to a collection — `ADD`, `REMOVE`, `MOVE`, `RESET` of an item, and custom operations registered on it (which must be listed too). Without it everything is allowed; `AllowOperations()` with no arguments locks both membership and order:

```csharp
builder.Collection(x => x.Animals, animals => animals
    .Item<Snake>(...)
    .Seed<Snake>()
    .AllowOperations(KVPatchOperations.Move));      // fixed set of items, reorderable
```

- Field patches inside the items (`SET /Animals/Snake/Name`) are unaffected.
- A `DISCARD` that would add, remove or reorder items of a restricted collection is rejected the same way.
- The restriction applies to **patches only**: the in-code collection API (`Create`, `RemoveById`, `MoveById`) used by application code and migrations stays available.

`KVCollectionDefinition.AllowedOperations` (`null` = unrestricted) and `IsOperationAllowed(...)` let a UI hide actions that would be rejected.

## Collection rules

Collections support count and aggregate validation rules, for example `MinCount(1)` and `AggregateSum(...)`. See [Validation](/behavior/validation#collection-rules).
