---
title: Schema migrations
sidebar_position: 3
---

# Schema migrations

As a layout evolves, persisted data has to move with it. Because KVBind stores values by canonical path rather than the serialized C# shape, **additive** changes — a new optional field, a new collection, a reordered UI — need no migration at all; old data simply reads through. Migrations are for changes that genuinely transform stored data: backfilling a value, removing a field, moving or renaming a path, or swapping a nested node's type.

A migration translates into a **migration commit** — the same replayable `KVCommit` as any edit, with `MigrationToVersion` set — so a snapshot stays reconstructible from its commit chain. Migrations are registered on the root definition, and each targets a schema version:

```csharp
builder.Migration(2, m => m
    .Backfill(KVTarget.Root.Seg("Status"), _ => "draft")   // new field, backfilled
    .Remove(KVTarget.Root.Seg("LegacyCode")));             // dropped field

builder.Migration(3, m => m
    .RenameSegment("General", "Header")                    // field-group key renamed
    .RenameField(KVTarget.Root.Seg("LineItems").AnyItem().Seg("Description"), "Details"));
```

## Targeting with `KVTarget`

`KVTarget` is a structural selector, not a regex. It resolves against the data, expanding collection rows as wildcards and filtering polymorphic instances by their stored `$type` discriminator — something a single-key pattern cannot express:

```csharp
// Every collection row (any item id):
KVTarget.Root.Seg("LineItems").AnyItem().Seg("Description")

// Only rows whose $type is a given token:
KVTarget.Root.Seg("LineItems").AnyItem(ofType: "DiscountLine").Seg("Amount")

// A field on one nested-node subtype only, leaving the other untouched:
KVTarget.Root.Seg("Party").OfType("PERSON").Seg("FullName")
```

Wildcard-aware renames carry whole subtrees, including ids and nested nodes under collection rows:

```csharp
m.RenameNode(KVTarget.Root.Seg("Party"), "Counterparty");   // rename a nested-node slot everywhere it occurs
```

## Replacing a nested node type

Swapping a nested node from one type to another (here `PERSON` → `COMPANY`) flips the `$type` discriminator and reshapes the fields: drop the old type's unique fields, add the new type's, leave common fields untouched.

```csharp
builder.Migration(4, m => m.ReplaceNestedType(
    KVTarget.Root.Seg("Party").OfType("PERSON"),
    toType: "COMPANY",
    reshape: r => r
        .Drop("FullName")                                          // PERSON-only field
        .Set("CompanyName", view => view.Sibling("FullName"))));   // COMPANY-only, seeded from the old value
```

- Reshape operations run in the order written, each seeing the result of the ones before it (a `Set(..., overwrite: false)` after a `Rename` keeps the renamed value).
- `Rename` moves a single field or a whole subtree, and throws if the destination already holds data — `Drop` the destination first to replace it.
- Value factories read the **pre-migration** data, so a new field can be seeded from an old one even after it was dropped.

## Applying migrations

```csharp
// Brings the snapshot up to the newest registered version — one chained commit per pending migration.
var commits = KVMigrator.Migrate(snapshot, definition);
```

Only migrations newer than the snapshot's `SchemaVersion` run, each as its own commit; an already-current snapshot does nothing. Applied migrations never re-run and the up-to-date check is O(1), so the list stays cheap no matter how long it grows.

Migrations run without a bound aggregate, so no change reactions run: write derived values explicitly as part of the migration.

## New aggregates

A brand-new aggregate is already in the newest layout, so mint its snapshot through the definition to stamp the current version — otherwise it defaults to `0`, would be mistaken for legacy data, and would be needlessly re-migrated:

```csharp
var snapshot = definition.NewSnapshot();   // born stamped at CurrentSchemaVersion
```

## Background migration in batches

For large data sets, `KVMigrator.MigrateBatchAsync` runs an optional async **prepare** phase once per migration over only the subset of a batch that still needs it, so external lookups for a backfill are fetched in one round-trip instead of per aggregate:

```csharp
builder.Migration(5, m => m
    .Prepare((subset, ct) => LoadRatesAsync(subset.Select(s => s.Key), ct))   // one batch fetch
    .Backfill(KVTarget.Root.Seg("Rate"),
        view => view.Context.PreparedAs<IReadOnlyDictionary<object, decimal>>()[view.Context.Key!]));

var subjects = aggregates.Select(a => new KVMigrationSubject { Key = a.Id, Snapshot = a.Snapshot }).ToList();
var results = await KVMigrator.MigrateBatchAsync(subjects, definition, cancellationToken: ct);
```

Subjects already at the latest version contribute no commits and trigger no prepare, so re-running a partially completed migration over already-migrated data is a no-op.
