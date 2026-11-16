---
title: Patch read-only members
sidebar_position: 5
---

# Patch read-only members

`PatchReadOnly()` marks a field, field group, collection or nested node as off-limits to patches: every built-in patch operation that targets the member or anything beneath it is rejected.

The member still changes through application code — typed setters, domain methods, migrations — so state that belongs to the aggregate's behavior (a workflow status, approvals, audit data, derived totals) cannot be written directly by a client.

```csharp
builder.Field(x => x.Status, f => f.PatchReadOnly());
builder.FieldGroup(x => x.Audit, audit => audit.Field(x => x.ApprovedBy), group => group.PatchReadOnly());
builder.Collection(x => x.Approvals, approvals => approvals.Item<Approval>(...).PatchReadOnly());
builder.NestedNode(x => x.Approver, approver => approver.Bind<PersonApprover>("PERSON", ...).PatchReadOnly());
```

## Enclosing objects

Operations on an object that *contains* a read-only member follow from the rule:

- `RESET` of a collection item, nested node or field group that contains a patch read-only member is rejected, because a reset rewrites every value beneath it.
- `DISCARD` is rejected when it would revert a draft change to a patch read-only member. Objects created in the same draft can still be discarded whole.
- `ADD`, `REMOVE`, `INIT` and `DROP` of an enclosing object stay available (the member gets its defaults, or goes away with the object). Mark the collection or nested node itself `PatchReadOnly()` to lock those too.

## Exposing state changes through operations

[Custom operations](/patching/custom-operations) are application code: they bypass `PatchReadOnly()`. Combining the two is the way to let a client change state only through its domain method:

```csharp
builder.Field(x => x.Status, f => f.PatchReadOnly());   // SET /Status is rejected
builder.Operation(x => x.Submit);                       // SUBMIT / is the only way to change it
```

`KVDefinition.IsPatchReadOnly` exposes the flag so a UI can render the member read-only.
