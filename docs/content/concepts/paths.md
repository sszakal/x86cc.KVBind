---
title: Paths
sidebar_position: 2
---

# Paths

Every value in a KVBind aggregate lives at a **canonical path**: the `[KVBind]` keys from the root to the value, joined with `/`. The same path is used in storage, patches, validation errors, change sets and change reactions.

## Building a path

| Member | Path segment(s) | Example |
| --- | --- | --- |
| Field on the root | its key | `Title` |
| Field group | its key, then the group's members | `General/Code` |
| Collection item | the collection key, then the item id | `LineItems/shipping` |
| Field on an item | … then the field key | `LineItems/shipping/Amount` |
| Nested node | its key, then the active subtype's members (no type token) | `Party/CompanyName` |

Keys may contain `A-Z`, `a-z`, `0-9` and `_`; item ids may also contain `-`. Everything is case-sensitive.

The active subtype of a nested node is **not** part of the path: `Party/CompanyName` stays the same path whichever subtype stored it. The subtype is recorded separately (see below).

## Leading slashes

Patch paths are conventionally written with a leading slash (`/LineItems/shipping/Amount`), and `/` alone addresses the root for [custom operations](/patching/custom-operations). The leading slash is optional: `LineItems/shipping/Amount` addresses the same field.

Paths KVBind *reports* — in validation errors, change sets, `KVChangeContext.ChangedPath` and storage keys — have no leading slash.

## Reserved keys

KVBind stores a little structure next to your values, under keys that start with `$` (and therefore can never clash with a `[KVBind]` key):

| Key | Holds |
| --- | --- |
| `<collection>/$items` | the ordered list of item ids, e.g. `["shipping", "handling"]` |
| `<collection>/<id>/$type` | the item's type token |
| `<nested node>/$type` | the active subtype's token; absent while the slot is empty |
| `$parent` | the child mark of an [inheriting](/workflows/inheritance) aggregate |

## What a snapshot looks like

The `Data` of a committed snapshot for a small agreement — one field group, two line items and a company party:

```text
Title                       "Services Agreement"
General/Code                "MSA"
LineItems/$items            ["shipping", "handling"]
LineItems/shipping/$type    "Line"
LineItems/shipping/Amount   13
LineItems/handling/$type    "Line"
Party/$type                 "COMPANY"
Party/CompanyName           "Contoso"
```

A flat dictionary keyed by path is what makes [additive schema changes free](/concepts/mental-model#canonical-storage-flexible-layout) and lets [migrations](/workflows/migrations) target data structurally. The JSON form of the same data is shown under [Persistence](/integration/persistence#json-format).
