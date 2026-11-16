---
title: Persistence
sidebar_position: 2
---

# Persistence

KVBind produces three kinds of data — snapshots, commits and drafts — and leaves storing them to your application. Identity, ownership, concurrency and the choice of database are yours. This page describes what to store and how the pieces fit together; the [sample application](/samples) is a complete implementation on PostgreSQL with Marten.

## What to store

| Data | Type | Lifetime |
| --- | --- | --- |
| The current committed state of an aggregate | `KVSnapshot` | one per aggregate, replaced on every commit |
| The history | `KVCommit` | appended on every commit (including [migration commits](/workflows/migrations)); never changed |
| A user's open draft | `KVOverlay.Changes` (a `KVDictionary`) plus the base commit id | one per user and aggregate, deleted on commit |
| A rebase in progress (optional) | `KVOverlay.RebaseTarget` and `KVOverlay.Conflicts` | while the user reviews the rebase |
| Pending [domain events](/behavior/domain-events) of an open draft (optional) | your event objects | until the draft is committed or discarded |

A snapshot can always be rebuilt by applying its commit chain, oldest first, to `definition.NewSnapshot()`, so the commits are the source of truth and the snapshot is a cache of their result. Most applications still store the snapshot, because loading one document is cheaper than replaying a long history.

## JSON format

All KVBind data types serialize with `System.Text.Json` without extra configuration. Every value is written with its .NET type, so it reads back as the same type. A snapshot (abridged):

```json
{
  "LastCommitId": "43021928-35c5-4b25-8260-322609a3cb53",
  "LastCommitTimestamp": "2026-01-01T00:00:00+00:00",
  "SchemaVersion": 3,
  "Data": {
    "Title":                     { "$type": "System.String, System.Private.CoreLib",   "value": "Services Agreement" },
    "LineItems/$items":          { "$type": "System.String[], System.Private.CoreLib", "value": ["shipping", "handling"] },
    "LineItems/shipping/$type":  { "$type": "System.String, System.Private.CoreLib",   "value": "Line" },
    "LineItems/shipping/Amount": { "$type": "System.Decimal, System.Private.CoreLib",  "value": 13 }
  }
}
```

A commit carries `CommitId`, `PreviousCommitId`, `User`, `Timestamp`, `MigrationToVersion` (for migration commits) and its `Changes` in the same format. In a commit or a draft, a deleted path or subtree is a tombstone:

```json
"LineItems/handling": { "$tombstone": true }
```

- Type names are assembly-qualified without version, culture or public key token, so values survive package upgrades. Resolved names are cached.
- Enums can be stored as names or numbers: values are written with your serializer options (for example Marten's `EnumStorage.AsString`) and both forms are read back.
- Any document database, a JSON column or a blob store works. For a relational schema, store snapshots, commits and drafts as JSON documents rather than mapping paths to columns.

## Loading and saving a draft

Store a draft as its changes and the id of the commit it is based on. To load it, find that base snapshot, create an overlay over a copy of it, and put the changes back:

```csharp
// Save
draft.BaseCommitId = overlay.BaseCommitId;            // = overlay.Snapshot.LastCommitId
draft.Changes = new KVDictionary(overlay.Changes);
draft.IsRebasing = overlay.IsRebasing;
draft.RebaseTarget = overlay.RebaseTarget?.Clone();
draft.Conflicts = [.. overlay.Conflicts];

// Load
var overlay = KVOverlay.Create(baseSnapshot.Clone(), draft.User);
overlay.Changes = new KVDictionary(draft.Changes);
if (draft.IsRebasing && draft.RebaseTarget is not null)
    overlay.RestoreRebaseState(draft.RebaseTarget.Clone(), draft.Conflicts);

var agreement = KVRootNode.Create<Agreement>(overlay, definition);
```

While the draft is up to date, its base snapshot is simply the aggregate's current snapshot. Once someone else commits, the draft still needs the snapshot *as it was* at its base commit. Either keep a copy of the superseded snapshot keyed by commit id while drafts depend on it (what the sample does), or rebuild it by replaying the commit chain up to `BaseCommitId`.

Copy dictionaries and snapshots (`new KVDictionary(...)`, `Clone()`) when moving them between your documents and an overlay, so that edits to one do not leak into a tracked document.

## Committing

A commit is valid only on top of the snapshot it was built from. `KVSnapshot.Apply(commit)` enforces this: it throws when `commit.PreviousCommitId` is not the snapshot's `LastCommitId`.

```csharp
if (draft.BaseCommitId != snapshot.LastCommitId)
    return Conflict("The draft is stale; rebase it first.");   // see Drafts and rebase

var agreement = KVRootNode.Create<Agreement>(overlay, definition);
var commit = agreement.CreateCommit(DateTimeOffset.UtcNow);    // throws KVChangeSetValidationException when invalid
var events = agreement.DequeueEvents();

snapshot.Apply(commit);
// In one transaction: save the snapshot, append the commit, delete the draft, publish or outbox the events.
```

Two users can pass the stale check at the same moment, so protect the snapshot write with your store's optimistic concurrency (a version column, an ETag, Marten's `UseOptimisticConcurrency`). The losing request then rebases and tries again.

## Migrating on read

Bring old aggregates up to the current schema when they are loaded, and persist the migration commits like any other commit:

```csharp
var migrationCommits = KVMigrator.Migrate(snapshot, definition);   // no-op when current
if (migrationCommits.Count > 0)
{
    // save the snapshot and append migrationCommits
}
```

Do this before opening drafts on the snapshot. See [Schema migrations](/workflows/migrations) for batch migration.

## Children of inheriting aggregates

A [child aggregate](/workflows/inheritance) must be bound with its parent's committed snapshot on every load until it is detached, so store the parent's id with the child.
