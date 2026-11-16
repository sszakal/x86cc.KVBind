---
title: Defaults and reset
sidebar_position: 4
---

# Defaults and reset

## Declaring defaults

| Member | Declaration |
| --- | --- |
| Field | `f.Default(value)` |
| Nested node | `DefaultType<TSubtype>()` or `DefaultType("TOKEN")` — the subtype must be bound |
| Collection | `Seed<TItem>(...)` for predefined items, or `Default(...)` |

## When defaults apply

Defaults are applied **once, when an object is born**, and only fill values that are still unset:

- `KVRootNode.CreateNew(...)` applies defaults across the whole new aggregate.
- Creating a collection item — in code or with an `ADD` patch — applies that item's defaults, including its default nested types and seeded sub-collections.
- An `INIT` patch, or assigning a nested node in code (`Party = new PersonParty()`), applies the defaults of the newly initialized nested node before change reactions run.

Binding an existing aggregate with `KVRootNode.Create(...)`, editing it, merging or rebasing **never** applies defaults, so previous changes are never overwritten.

## Resetting a subgraph

`node.Reset()` — or a `RESET` patch on the node's path — replaces a node's data with a freshly born object of the same type, in place. Everything stored under the node is wiped, its `$type` is kept, and its defaults are applied again, including default nested types and seeded sub-collections (extra items in those sub-collections are dropped).

```csharp
zoo.Patch(KVPatchOperation.Reset("/Animals/Lizard"));   // same id, same position, fresh data
zoo.Patch(KVPatchOperation.Reset("/Owner"));            // same nested type, fresh data
zoo.Patch(KVPatchOperation.Reset("/Settings"));         // field group declared Resettable()
```

A reset collection item keeps its id and its position, and the change set reports field-level edits of the same item rather than a removal and an addition. In contrast, removing an item and adding one with the same id also yields fresh data but appends the item at the end.

### What can be reset by a patch

| Target | `RESET` allowed |
| --- | --- |
| Collection item | Yes, unless the collection's `AllowOperations(...)` omits `RESET` |
| Nested node | When it is initialized |
| Field group | Only when declared `Resettable()` |
| Field, root | Never — use `Clear()` to drop the root's draft changes |

A `RESET` is also rejected when the node contains a [patch read-only](/dsl/patch-read-only) member. In code, `Reset()` is available on any non-root node.

## Reset versus discard

- **Reset** (`Reset()`, `RESET`) returns a node to its *defaults*.
- **Discard** (`Discard(path)`, `DISCARD`) returns a path to its *last committed* state. On a collection item that includes its membership: an item the draft added disappears, one it removed comes back at its committed position, and one it only edited keeps its place.
- **Clear** (`Clear()`) drops every draft change of the root.

All three raise a single `Replaced` notification per affected [change reaction](/behavior/change-reactions#keeping-derived-values-fresh).
