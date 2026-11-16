---
title: Domain events
sidebar_position: 3
---

# Domain events

A node records what happened in business terms with `Raise(...)`, from a domain method or a change reaction on the root, a field group, a collection item or a nested node. Events collect on the root aggregate in the order raised.

```csharp
public void Submit()
{
    var previous = this.Status;
    this.Status = "submitted";
    this.Raise(new ContractSubmitted(previous));
}
```

The application reads them with `DequeueEvents()`, which returns the pending events and empties the list:

```csharp
var commit = contract.CreateCommit(DateTimeOffset.UtcNow);
var events = contract.DequeueEvents();
// persist the commit and publish the events in one transaction (or through an outbox)
```

Because patch operations call the same methods (`builder.Operation(x => x.Submit)`), a `SUBMIT /` patch and a command handler raise the same event. An event is any object; KVBind only collects them. Pending events are visible on `root.PendingEvents`.

## Lifetime

Events live in memory on the bound aggregate. They are **not** part of the overlay or the commit, and KVBind does not persist them.

- When a draft stays open across requests, dequeue after each request and store the events with the draft, then publish them when the draft is committed.
- `Clear()` drops the pending events together with the draft; `Discard(path)` and `Reset()` keep them.
- A reaction that raises events also runs for `Replaced` notifications, so check `context.Kind` there if the event should only follow a real edit.
