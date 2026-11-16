---
title: Inheritance
sidebar_position: 2
---

# Inheritance

A child aggregate can inherit root members from a parent aggregate — for example program terms shared by every application under a program. Inherited members read from the parent's committed snapshot and are read-only in the child; when the parent changes, the child sees the new values on its next bind.

## Declaring inherited members

Mark root members with `Inherited()`. Fields, field groups, collections and nested nodes can be inherited, and only on the root definition (anywhere else, `Build()` throws).

```csharp
builder.Field(x => x.ProgramName, f => f.Inherited());
builder.FieldGroup(x => x.Terms, terms => terms.Field(x => x.Limit), options => options.Inherited());
```

On an aggregate without a parent (a master), inherited members are ordinary editable members. A default on an inherited member applies only to masters; a child takes the parent's value instead.

## Creating and binding a child

```csharp
// A brand-new child of a bound parent: a fresh, child-marked overlay.
var child = parent.CreateChild<Program>();
child.ApplicationNumber = "A-001";                    // edit the child's own members

var commit = child.CreateCommit(DateTimeOffset.UtcNow);
var childSnapshot = child.RootModel().Snapshot;
childSnapshot.Apply(commit);                          // persist childSnapshot and the commit

// Every later load must supply the parent's snapshot.
var overlay = KVOverlay.Create(childSnapshot, user: "alice");
var bound = KVRootNode.Create<Program>(overlay, definition, parentSnapshot);
```

The child mark is stored in the child's data, so binding a child without its parent throws.

## Detaching

`root.Detach()` severs the link: it copies every inherited value from the parent into the child's draft, so they become the child's own editable data, and removes the child mark. Commit the draft to persist it, and stop supplying the parent on later binds.
