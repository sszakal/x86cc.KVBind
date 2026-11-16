---
title: Custom operations
sidebar_position: 2
---

# Custom operations

Custom operations let a client invoke domain behavior instead of writing values. They map a patch operation name to a method of a node, so a command handler and a patch run exactly the same logic, reactions and validation.

## Operations on nodes

`Operation(...)` on a node definition maps a custom patch operation to a method of that node. The patch path addresses the node the operation is declared on: the root (`/`), a field group, a collection item, or an initialized nested node (the operation must be declared on its active subtype).

```csharp
builder.Field(x => x.Status, f => f.PatchReadOnly());
builder.Operation(x => x.Submit);                                   // SUBMIT /
builder.Operation<string>("CHOOSE_GRANT", x => x.ChooseGrant);      // CHOOSE_GRANT /  "EXCLUSIVE"

builder.Collection(x => x.Approvals, approvals => approvals
    .Item<Approval>(item => item.Operation(x => x.Approve))         // APPROVE /Approvals/legal
    .PatchReadOnly());

agreement.Patch(
    KVPatchOperation.Custom("CHOOSE_GRANT", "/", "EXCLUSIVE"),
    KVPatchOperation.Set("/Grant/Territory", "EU"),
    KVPatchOperation.Custom("SUBMIT", "/"));
```

- The name defaults to the method name in upper case.
- A method without parameters rejects a patch value; a method with one parameter receives the patch value converted to that type (JSON payloads are deserialized).
- `KVNodeDefinition.OperationNames` lists a node's operations, so a UI can offer them.

### Relation to patch restrictions

Custom operations are application code. They may be sent to — and may change — `PatchReadOnly()` members, and an operation on a collection item is not subject to the collection's `AllowOperations(...)`. That makes `PatchReadOnly()` plus an operation the way to expose a state change only through its domain method.

## Operations on collections

A collection can register operations that act on the collection as a whole. The method lives on the collection's parent node and receives the patch value:

```csharp
builder.Collection(x => x.LineItems, collection =>
{
    collection.Operation<GroupLineItems>("GROUP", x => x.GroupLineItems);
    collection.Item<AgreementLineItem>(item => item.Field(x => x.Amount));
});
```

A collection operation is a structural operation: when the collection uses `AllowOperations(...)`, list it there too.
