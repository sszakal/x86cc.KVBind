---
title: Patch operations
sidebar_position: 1
---

# Patch operations

Patches are how clients edit an aggregate. Each operation addresses a canonical path and runs sequentially in the order supplied, so one request can create a row and then set fields on it.

```csharp
var itemId = Guid.NewGuid();

var result = agreement.Patch(
    KVPatchOperation.Add("/LineItems", new KVAddPatchPayload(itemId)),
    KVPatchOperation.Set($"/LineItems/{itemId:D}/Description", "Implementation services"),
    KVPatchOperation.Set($"/LineItems/{itemId:D}/Amount", 120m));

var validation = result.Validate();
```

Patches run through the same runtime as typed setters: defaults, change reactions, validation and domain events behave identically.

## Built-in operations

| Operation | Factory | Target | Effect |
| --- | --- | --- | --- |
| `SET` | `Set(path, value)` | field | Updates the value, converted to the field's type. |
| `UNSET` | `Unset(path)` | field | Removes the value from the draft. |
| `ADD` | `Add(path, payload)` | collection | Creates an item with a client-provided id (any valid id, or a GUID) and applies its defaults. |
| `REMOVE` | `Remove(path)` | collection item | Removes the item. |
| `MOVE` | `Move(path, toIndex)` | collection item | Reorders the item. |
| `INIT` | `Init(path, typeToken)` | nested node | Selects the subtype and applies its defaults. |
| `DROP` | `Drop(path)` | nested node | Clears the slot. |
| `DISCARD` | `Discard(path)` | any path | Drops the draft's changes at and beneath the path, so it reads as the last commit again. |
| `RESET` | `Reset(path)` | item, nested node, resettable group | Replaces the node with fresh data and its defaults, in place. |

Built-in names are reserved: custom operations cannot override them. Structural operations on a collection can be restricted with [`AllowOperations(...)`](/dsl/collections#restricting-structural-patch-operations), and any member can be locked with [`PatchReadOnly()`](/dsl/patch-read-only).

### `DISCARD` on collection items

Discarding a collection item reverts its membership too:

- an item the draft **added** disappears;
- an item the draft **removed** comes back at its committed position (after its nearest committed predecessor that is still present, else first);
- an item the draft only **edited** keeps its place and gets its committed values back.

The revert is subject to `AllowOperations` (it needs `REMOVE` or `ADD` respectively).

## Value types

A `SET` value is converted to the declared type of its field (the model property's type, exposed as `KVFieldDefinition.ValueType`) before it is stored, so a patch can never leave a value the typed property cannot read. A value that does not fit is rejected and nothing is written:

- Lossless numeric conversions are accepted (`42` into a `decimal` field, `7.0` into an `int` field); `1.5` into an `int` field is not.
- Strings are parsed for non-string fields: numbers (`"12.50"`), dates, GUIDs, enum names, and raw JSON for structured fields (`"[1,2,3]"` into an `int[]`).
- An enum field accepts a member's name or number, and rejects a number that is not a declared member (for a `[Flags]` enum, a combination of declared members is accepted).
- `JsonElement` values are deserialized to the field type, so a web API can pass the request value through unchanged.
- Text into a number, a number into a string field, or a number into a `bool` is rejected.
- `null` clears the field.

### Fields with allowed values

On a single-value field with allowed values, the definition resolves the value (`KVAllowedValuesDefinition.TryResolve`):

1. **A token first.** Tokens are the wire form, so `10` means the value whose token is 10, even when 10 is also an enum member's number or another allowed value.
2. **Else the value as the field type**, losslessly: `"2"` or `2.0` for an `int`, a member name such as `"High"` for an enum.

A JSON array or object on a single-value field is rejected. An unknown value is kept in the draft for validation to report (`allowed_values`) only when the tokens are text; otherwise it is rejected.

## Change sets

`root.Patch(...)` returns a `KVPatchResult` whose `Changes` are the change set of **that patch only**: its operations plus the writes of the change reactions they triggered, recorded as it runs and fixed when it returns. Each `KVChangeDelta` has a `Path`, a `ChangeType` and, for fields, `OldValue` / `NewValue`.

- A path the patch added, removed or replaced (`RESET`, `DISCARD`) stands for everything beneath it.
- An item added and removed again in the same patch leaves nothing.
- A field written back to its old value (sequences compared by content) is no change.

Everything the draft holds against its snapshot — earlier patches and sessions included — is `root.GetAllChanges()`.

```csharp
var result = agreement.Patch(KVPatchOperation.Set("/Title", "Renewed agreement"));

foreach (var change in result.Changes)
    Console.WriteLine($"{change.ChangeType} {change.Path}: {change.OldValue} -> {change.NewValue}");

var pending = agreement.GetAllChanges().Changes;
```
