---
title: Change reactions
sidebar_position: 1
---

# Change reactions

Change reactions let a definition react to changes, whether they come from typed setters in code or from patches. They are the place to maintain derived values such as totals and summaries.

```csharp
builder.OnChange(
    path => path.Collection(x => x.LineItems).Field(x => x.Amount),
    x => x.RecalculateSummary);
```

The reaction method lives on the node and receives a context:

```csharp
public void RecalculateSummary(KVChangeContext<Agreement> context)
{
    Summary = $"Changed: {context.ChangedPath}";
}
```

Reactions can be declared on the root, field groups, collection items and nested node subtypes, and bubble up through all of them.

## Change kinds

Structural changes notify reactions too, at the path of the item or slot. `context.Kind` tells them apart:

| `Kind` | Raised by | `OldValue` | `NewValue` |
| --- | --- | --- | --- |
| `Value` | field set or unset | previous value | new value |
| `Added` | collection item created (`Create`, `ADD`, seeds) | `null` | the item |
| `Added` | nested node initialized (setter, `INIT`, default type) | replaced type token, or `null` | the active node |
| `Removed` | collection item removed | item id | `null` |
| `Removed` | nested node dropped (setter to `null`, `DROP`) | dropped type token | `null` |
| `Moved` | collection item reordered (`MoveById`, `MOVE`) | old index | new index |
| `Replaced` | data replaced wholesale (`Reset`, `Discard`, `Clear`, `NotifyReplaced`) | `null` | `null` |

Watch structural changes with `Any()`:

```csharp
builder.OnChange(path => path.NestedNode(x => x.Party).Any(), x => x.OnPartyChanged);
builder.OnChange(path => path.Collection(x => x.LineItems).Any(), x => x.OnLineItemsChanged);
```

A newly created item or initialized nested node is reported after its defaults are applied. Dropping an empty slot or moving an item to its current position raises nothing.

## Keeping derived values fresh

`Reset()`, `Discard(path)` and `Clear()` (and their `RESET` / `DISCARD` patches) change data without a notification per value. Instead they raise `Replaced` once for every reaction that watches the affected path or anything beneath it, innermost nodes first, so a reaction that maintains a derived value — a line total, a grand total — runs again and recomputes from the current state. `ChangedPath` is the replaced path (empty when the reacting node itself was replaced).

A rebase and migrations work on overlays and snapshots, without a bound aggregate, so they cannot run reactions. After a rebase finishes, bind the draft and call `NotifyReplaced()` before persisting it:

```csharp
overlay.FinishRebase();
var agreement = KVRootNode.Create<Agreement>(overlay, definition);
agreement.NotifyReplaced();          // or NotifyReplaced("/LineItems") to limit it to a subtree
```

Migrations should write derived values explicitly as part of the migration.

## Execution

- Reaction execution state is scoped to the root aggregate, so separate edits start cleanly.
- Writes made by reactions during a patch are part of that patch's [change set](/patching/operations#change-sets).
- KVBind detects active reaction cycles such as `A -> B -> A` and keeps a maximum chain-length guard for runaway non-repeating chains.
