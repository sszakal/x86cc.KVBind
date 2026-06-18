---
name: author-migration
description: Guide for writing KVBind schema migrations with KVTarget selectors, backfill/remove/rename/replace operations, version stamping, and batch prepare. Use when persisted data must transform as the schema evolves.
---

# Authoring a schema migration

KVBind stores values by canonical path, so **additive** changes (new optional field, new collection, reordered UI) need no migration — old data reads through. Write a migration only when stored data must actually move: backfill, remove, rename/move a path, or swap a nested-node type.

Each migration becomes a replayable `KVCommit`, so a snapshot stays reconstructible from its commit chain.

## Register against a schema version

```csharp
builder.Migration(2, m => m
    .Backfill(KVTarget.Root.Seg("Status"), _ => "draft")
    .Remove(KVTarget.Root.Seg("LegacyCode")));

builder.Migration(3, m => m
    .RenameSegment("General", "Header")
    .RenameField(KVTarget.Root.Seg("LineItems").AnyItem().Seg("Description"), "Details"));
```

Only migrations newer than a snapshot's `SchemaVersion` run; each runs as its own commit; applied migrations never re-run (O(1) up-to-date check).

## KVTarget — structural selector, not regex

It resolves against the data: expands collection rows as wildcards and filters polymorphic instances by their stored `$type`.

```csharp
KVTarget.Root.Seg("LineItems").AnyItem().Seg("Description")              // every row
KVTarget.Root.Seg("LineItems").AnyItem(ofType: "DiscountLine").Seg("Amount")  // rows of one $type
KVTarget.Root.Seg("Party").OfType("PERSON").Seg("FullName")             // one nested subtype only
m.RenameNode(KVTarget.Root.Seg("Party"), "Counterparty");              // carries the whole subtree
```

## Replacing a nested-node type

Flip the `$type`, reshape fields, seed new from old (fields are read from pre-migration data):

```csharp
builder.Migration(4, m => m.ReplaceNestedType(
    KVTarget.Root.Seg("Party").OfType("PERSON"),
    toType: "COMPANY",
    reshape: r => r
        .Drop("FullName")
        .Set("CompanyName", view => view.Sibling("FullName"))));
```

## Applying

```csharp
var commits = KVMigrator.Migrate(snapshot, definition);   // one chained commit per pending migration
```

## Two correctness traps

1. **Stamp new aggregates.** Mint via `definition.NewSnapshot()` so it is born at `CurrentSchemaVersion`. A raw `new KVSnapshot()` defaults to version `0` and gets needlessly re-migrated as if it were legacy data.
2. **Batch prepare for external lookups.** For large data sets use `KVMigrator.MigrateBatchAsync` with a `.Prepare(...)` phase — it fetches external data once per batch over only the subset still needing migration, instead of per aggregate. Already-current subjects produce no commits and trigger no prepare, so re-running a partial batch is a no-op.

## Always

Add migration tests under `Core/Migrations` (model after `MigrationScenarioTests` / `MigrationCommitTests`) and verify the no-op and re-run cases, then run the `run-tests` skill.
