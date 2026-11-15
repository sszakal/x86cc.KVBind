# KVBind performance on large and deep forms

Two kinds of form (see [README.md](README.md)):

- **Large, flat:** the generated `large-{N}` forms at 100, 500, 1000 and 2000 section fields: 25-field sections
  cycling through ten field kinds with their rules, plus 50 line items with a reaction-maintained total.
- **Deep, recursive:** `deep`, a component tree that can grow without limit, with about 1,000 field values at
  depth 1, 2, 3 and 4.

Measured on an AMD Ryzen 9 9950X with .NET 10.0.2 and Postgres 18 in a local container, in November 2026.
Every table below is from one run of the current code.

**In short:** KVBind's own work grows linearly with the size of the form and stays small. At 2000 fields a
request spends **26 µs** binding, **27 µs** binding and patching one field, **0.23 ms** validating, and
**1.5 ms** applying 2000 field changes. Depth costs little: the same stages on a four-level tree of 1,000
values take 26 µs, 32 µs, 0.26 ms and 2.7 ms. The rest of a request is storage I/O (Marten/Postgres) and
turning the aggregate into JSON for the client.

## 1. Large forms: in-process stages (BenchmarkDotNet)

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*LargeForm*"`. The aggregate is
committed with every field set plus 50 lines. "100 changes" means a draft with 100 changed fields. Mean
time per operation, with allocation in parentheses.

| Stage | 100 fields | 500 | 1000 | 2000 |
|---|---:|---:|---:|---:|
| bind (load → typed root) | 8.0 µs (28 KB) | 11.8 µs (41 KB) | 16.3 µs (61 KB) | 26.4 µs (98 KB) |
| bind + patch 1 field | 8.6 µs (30 KB) | 12.2 µs (42 KB) | 16.9 µs (63 KB) | 27.2 µs (100 KB) |
| bind + patch 100 fields | 76 µs (127 KB) | 79 µs (140 KB) | 85 µs (161 KB) | 99 µs (198 KB) |
| bind + patch every field | 78 µs (127 KB) | 355 µs (576 KB) | 708 µs (1.0 MB) | 1.49 ms (2.1 MB) |
| validate (100 changes) | 53 µs (55 KB) | 88 µs (86 KB) | 138 µs (125 KB) | 229 µs (203 KB) |
| compute deltas (100 changes) | 21 µs (92 KB) | 30 µs (134 KB) | 41 µs (186 KB) | 70 µs (290 KB) |
| commit (validate + build, 100 changes) | 55 µs (58 KB) | 89 µs (88 KB) | 136 µs (128 KB) | 234 µs (206 KB) |
| rebase with 100 conflicts | 34 µs (122 KB) | 42 µs (159 KB) | 56 µs (216 KB) | 79 µs (321 KB) |
| project all fields to JSON (sample) | 75 µs (194 KB) | 143 µs (327 KB) | 224 µs (494 KB) | 384 µs (823 KB) |

- Binding is cheap because it is lazy: values are read from the overlay when accessed. Patching costs
  about 0.7 µs per changed field, including type conversion, allowed values, `PatchReadOnly`, reactions and
  recording the patch's change set (`KVPatchResult.Changes`).
- Validation is a full evaluation, so it grows with the size of the form, not with the number of changes.
- Rebase cost depends on the commits it folds in and on the number of conflicts, hardly on the form's size.

## 2. Large forms: end to end over HTTP

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- load http://localhost:5101 5 report.md`
runs requests one after another against the sample. The API was a Debug build under Aspire, and Postgres was
local. Each round runs the full draft workflow for one aggregate. The table shows the p50 round trip, the API's
own total and the stages it reports in `Server-Timing` (the ⏱ panel in the UI shows the same per request).

| 2000 fields | p50 | server | biggest stages (p50 ms) | response |
|---|---:|---:|---|---:|
| create | 6.5 ms | 6.3 | persist 4.2, project 0.7, commit 0.5, bind 0.3 | 32 KB |
| patch all 2000 fields | 20.6 ms | 19.0 | project 6.4, persist 6.0, **patch 3.2**, load 0.8 | 231 KB |
| patch 1 field (after 2000 + 50 lines) | 24.7 ms | 22.8 | project 11.7, persist 6.6, load 4.9, **patch 0.02** | 271 KB |
| get draft | 17.7 ms | 15.8 | project 9.9, load 5.2, **bind 0.06** | 271 KB |
| validate | 5.1 ms | 5.0 | load 3.6, **validate 1.2** | — |
| commit | 27.6 ms | 27.4 | persist 12.7, load 7.1, project 6.7, **commit 1.1** | 52 KB |
| get snapshot | 4.9 ms | 4.6 | load 3.4, project 0.9 | 51 KB |

At 100 fields the same calls take 2–10 ms. The KVBind stages are in bold: together they are under 5% of any
request, except the full 2000-field patch (17%). The rest is:

- **persist:** Marten writes whole documents. A draft document holds the draft's changes (here every field
  was changed, so that is the whole form); a commit writes the new snapshot, the change set, and a copy of
  the snapshot it replaced (see section 7).
- **load:** reading those documents back. Each stored value carries its type name and is deserialized on
  its own.
- **project:** for a draft, the JSON tree of the data plus a labelled change row per changed path. The
  overlay drawer in the UI shows these rows; at 2000 changed fields that is about 2,350 rows.

## 3. Deep, recursive forms

`deep` is a tree that can grow without limit: components (collection items) with their own `Measurements`
collection and an empty `Detail` slot; an `ASSEMBLY` in that slot holds components again, a `PURCHASED` part
ends the branch. Costs roll up through every level by change reactions (each assembly's `Subtotal`, the root's
`TotalCost`). The benchmark shapes have about 1,000 field values and the same 45 components (42 at depth 3)
at every depth, so only the depth changes: 45 flat (1,084 values), 15 × 3 (1,069), 6 × 7 (994) and 3 × 15
(1,063). Most values sit in the deepest level.

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*DeepForm*"`

| Stage | depth 1 | 2 | 3 | 4 |
|---|---:|---:|---:|---:|
| bind | 32.0 µs (121 KB) | 23.0 µs (69 KB) | 22.0 µs (47 KB) | 26.5 µs (50 KB) |
| project the whole tree to JSON (sample) | 199 µs (508 KB) | 207 µs (556 KB) | 197 µs (573 KB) | 225 µs (683 KB) |
| build the tree from empty (≈1,250 ADD / INIT / SET) | 2.49 ms (4.1 MB) | 2.35 ms (5.8 MB) | 3.23 ms (7.1 MB) | 4.91 ms (10.8 MB) |
| bind + patch 1 field at the deepest level | 34.3 µs (123 KB) | 25.2 µs (77 KB) | 25.2 µs (61 KB) | 32.1 µs (71 KB) |
| bind + patch 1 deepest price (updates every level above) | 35.3 µs (125 KB) | 28.3 µs (81 KB) | 30.0 µs (67 KB) | 38.1 µs (80 KB) |
| bind + patch every leaf's unit cost | 101 µs (206 KB) | 142 µs (252 KB) | 179 µs (288 KB) | 248 µs (393 KB) |
| bind + patch every field | 1.09 ms (1.6 MB) | 1.54 ms (2.2 MB) | 2.10 ms (2.8 MB) | 2.72 ms (4.3 MB) |
| bind + drop the first top-level subtree | 64 µs (224 KB) | 71 µs (238 KB) | 52 µs (192 KB) | 61 µs (235 KB) |
| validate (100 changes spread over the tree) | 124 µs (146 KB) | 157 µs (191 KB) | 182 µs (229 KB) | 258 µs (310 KB) |
| compute deltas (100 changes) | 61 µs (278 KB) | 72 µs (368 KB) | 80 µs (435 KB) | 99 µs (580 KB) |
| commit (validate + build, 100 changes) | 126 µs (148 KB) | 158 µs (194 KB) | 183 µs (232 KB) | 259 µs (313 KB) |
| rebase with 100 conflicts | 95 µs (338 KB) | 120 µs (377 KB) | 128 µs (369 KB) | 155 µs (394 KB) |

- **Depth costs little.** Binding, patching one field and projecting are flat from depth 1 to 4. Validate,
  deltas and commit grow by about 2× because a deeper tree of the same size has more nodes (each assembly
  is one more nested node with its own rules), and its keys are longer paths.
- **A change four levels down is cheap to propagate.** Setting the deepest price updates three assembly
  subtotals and the root total through cascading reactions for about 6 µs more than a plain write.
- **Writes get dearer with depth** (patch every field 1.1 → 2.7 ms): each write resolves a longer path,
  notifies more ancestors, and a cost change cascades through more levels.

End to end over HTTP (`-- load … deep`), depth 4, p50:

| deep, depth 4 | p50 | biggest stages (p50 ms) | response |
|---|---:|---|---:|
| build the tree (1,239 operations) | 20.4 ms | **patch 7.5**, project 5.2, persist 4.9 | 185 KB |
| patch 1 deepest price | 13.4 ms | project 5.2, persist 4.9, load 2.0, **patch 0.09** | 185 KB |
| get draft | 8.0 ms | project 5.3, load 1.9 | 185 KB |
| validate | 2.9 ms | load 1.9, **validate 0.7** | — |
| commit | 18.9 ms | persist 9.5, project 5.9, load 2.4, **commit 0.7** | 27 KB |

## 4. A long history on the deep form

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- history http://localhost:5101 1000 3 report.md`
builds the depth-4 tree through the API, a few edits per commit (open draft → patch 3 operations → commit),
then keeps re-editing every field until the aggregate has 1,000 commits; the tree is complete after 384.
Meanwhile four other users open drafts that end up 1, 10, 100 and 1,000 commits behind; each edits fields
the later commits change too and adds a component of its own. At the end they rebase, resolve, and commit.

Each commit as the history grows (p50 ms, round trip):

| Commits | open draft | patch | commit | commit's biggest stages |
|---|---:|---:|---:|---|
| 1–10 | 3.6 | 3.8 | 4.3 | persist 3.4, load 0.6 |
| 11–100 | 4.0 | 4.3 | 5.3 | persist 3.9, load 0.9 |
| 101–500 | 6.0 | 6.5 | 9.9 | persist 6.1, load 2.0, commit 0.6 |
| 501–1,000 | 7.2 | 7.5 | 12.7 | persist 8.4, load 2.2, commit 0.7 |

**The history's length costs nothing per request.** The growth follows the tree filling up (load and persist
carry the whole aggregate); the tree is complete after commit 384, so the last bucket is the steady state.

Reading at the end: the snapshot 2.5 ms (27 KB); a page of history (`?offset=&limit=50`) 2.4–2.8 ms (33–37 KB)
from either end of the 1,005 commits; the largest page, 500 commits, 6.1 ms (334 KB).

Rebasing the drafts left behind, over HTTP:

| Behind by | reviewed | conflicts (derived, auto-resolved) | begin rebase | resolve one | finish | commit |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 7 | 7 (4) | 21 ms | 13 ms | 15 ms | 13 ms |
| 10 | 32 | 18 (4) | 13 ms | 12 ms | 15 ms | 15 ms |
| 100 | 259 | 11 (2) | 16 ms | 15 ms | 21 ms | 19 ms |
| 1,000 | 191 | 2 (2) | 31 ms | — | 17 ms | 16 ms |

The four drafts' components all survive the merges (the root ends with 7 top-level components); every
derived subtotal and total that both sides rewrote is resolved by the service and recomputed on finish.

The same in process (`--filter "*DeepHistory*"`): a draft branched off the complete tree with 20
conflicting edits and its own component, rebased over `Behind` commits of 3 edits.

| Behind by | begin rebase | begin + resolve + finish + recompute + commit | replay the commits onto the base |
|---:|---:|---:|---:|
| 1 | 120 µs (342 KB) | 0.77 ms (0.9 MB) | 25 µs |
| 10 | 128 µs (356 KB) | 0.91 ms (1.0 MB) | 26 µs |
| 100 | 232 µs (495 KB) | 1.15 ms (2.6 MB) | 33 µs |
| 1,000 | 615 µs (782 KB) | 1.76 ms (5.6 MB) | 109 µs |

Folding 1,000 commits (3,000 changes) into a rebase takes 0.6 ms; most of an end-to-end rebase is the
recompute (binding and `NotifyReplaced` over the whole tree) and the commit's validation, which do not depend
on how far behind the draft was.

## 5. Memory

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- memory report.md`. **Retained** is what stays
alive, measured on the live heap after full collections and averaged over 24 instances, each loaded from its
stored JSON as a request would load it. (The tables above show what each stage *allocates* in passing.)
"Field values" counts the 50 lines of the large forms too.

| Form | field values | loaded snapshot | + bound root | + every node visited | + after patching 100 fields | JSON tree for the client | bytes per field value |
|---|---:|---:|---:|---:|---:|---:|---:|
| large-100 | 453 | 90 KB | 11 KB | 11 KB | 12 KB | 131 KB | 203 |
| large-500 | 853 | 140 KB | 13 KB | 13 KB | 12 KB | 229 KB | 168 |
| large-1000 | 1,353 | 231 KB | 15 KB | 15 KB | 12 KB | 352 KB | 174 |
| large-2000 | 2,353 | 415 KB | 19 KB | 19 KB | 12 KB | 594 KB | 180 |
| deep, depth 1 | 1,084 | 267 KB | 69 KB | 83 KB | 19 KB | 336 KB | 252 |
| deep, depth 2 | 1,069 | 308 KB | 23 KB | 96 KB | 87 KB | 335 KB | 294 |
| deep, depth 3 | 994 | 336 KB | 10 KB | 100 KB | 106 KB | 313 KB | 345 |
| deep, depth 4 | 1,063 | 414 KB | 5 KB | 120 KB | 132 KB | 335 KB | 398 |

- **The data is the memory; the model on top is small.** A loaded snapshot costs about 170–200 bytes per
  field value on a flat form. A bound root adds 11–19 KB on a flat form whatever is read, because field
  groups are plain objects and values stay in the snapshot.
- **Depth costs memory per value, not per node.** The same ~1,000 values take 267 KB at depth 1 and 414 KB
  at depth 4 (398 bytes per value), because every value is stored under its full path and deep paths are
  long strings. Typed nodes are created on demand: a deep tree binds for 5 KB, and visiting every node of
  45 components with their measurements and details adds about 120 KB.
- **The JSON tree sent to a client is as big as the snapshot itself** (313–594 KB retained while it is
  built), which is why `project` is the largest stage of most requests.

Stored size, as JSON in Postgres:

| Form | snapshot document | draft document, 100 changes | the same draft with its base inside (before) |
|---|---:|---:|---:|
| large-100 | 43 KB | 8 KB | 52 KB |
| large-2000 | 211 KB | 8 KB | 219 KB |
| deep, depth 1 | 135 KB | 8 KB | 143 KB |
| deep, depth 4 | 208 KB | 13 KB | 221 KB |

### Capacity: completely filled forms

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- capacity report.md`. **Held** is what one request
keeps alive while it works on the aggregate (loaded from its stored JSON, bound, every node visited); **per
request** is everything one draft edit allocates (load, bind, patch one field, build and serialize the JSON
response), garbage included.

| Form | stored keys | stored JSON | held in memory | of which path strings | allocated per request | 100 held | 1,000 held |
|---|---:|---:|---:|---:|---:|---:|---:|
| flat, 1000 fields + 50 lines | 1,405 | 122 KB | 246 KB | 73 KB (30%) | 1.7 MB | 24 MB | 240 MB |
| flat, 2000 fields + 50 lines | 2,405 | 211 KB | 434 KB | 122 KB (28%) | 2.9 MB | 42 MB | 423 MB |
| depth 4, 1,063 values (45 components) | 1,355 | 208 KB | 534 KB | 255 KB (48%) | 2.0 MB | 52 MB | 521 MB |
| depth 4, 2,122 values (90 components) | 2,705 | 416 KB | 1,071 KB | 510 KB (48%) | 4.1 MB | 105 MB | 1,046 MB |

- **Requests per second are not aggregates in memory.** What is held at once is requests per second ×
  request time: at about 20 ms per request, 100 requests/s hold about 2 aggregates and 1,000 requests/s
  about 20 (2–21 MB for the largest form). Keeping 100 or 1,000 aggregates loaded, as a cache would, costs
  the "held" columns.
- **The pressure is allocation, not residence.** 1,000 requests/s on the 2,000-field forms allocate 3–4 GB
  of short-lived objects per second, most of it the JSON (stored document in, response out). The
  garbage collector reclaims it, but that churn is what a server would be sized by.
- **Path strings are not shared.** Every loaded aggregate holds its own copy of every key, so paths are
  28–30% of a flat form and 48% of a deep one. The distinct path segments of a form are only 3–15 KB; see
  section 8.

## 6. Hot spots found, and what was done

Before → after figures are from the run in which each change was made.

| Found | Where | Change | Effect |
|---|---|---|---|
| `Patch` computed the whole draft's deltas on every call, even when the caller never reads them | KVBind `KVRootNode.Patch` | `KVPatchResult.Changes` is the patch's own change set, recorded from the change notifications as it runs; the whole draft's changes are `GetAllChanges()` | patch 1 field at 2000 fields **95.5 → 27 µs**, 381 → 100 KB |
| Every stored value resolved its assembly-qualified type name again when read | KVBind `KVValueJsonConverter` | resolved types are cached by name | draft `load` stage 5.9 → about 4.3 ms at 2000 fields |
| Dropping (or re-initializing) a nested slot removed its keys one by one, and every removal rescanned all the draft's keys: quadratic in the subtree size | KVBind `KVNode.ClearNestedNodeModel` | one pass over the keys (`KVOverlay.ResetSubtree`) | drop a 15-component subtree **2.2 ms / 37.5 MB → 61 µs / 235 KB** |
| Every write checks each reaction's path pattern at every ancestor, and each check split the path into a new array | KVBind `KVPathPattern.Matches` | compares segments in place, no allocation | patch every field at depth 4: 4.31 → 2.86 ms, 10.1 → 4.0 MB |
| Change rows looked for a path's descendants by scanning every key, per change | sample `AggregateService` | field paths skip the check; other paths use a sorted key index | draft `project` 14 → 5 ms after a full patch at 2000 fields |
| Rebase read the whole history to find the commits since the draft's base | sample `AggregateService` | reads only the change sets since the base's timestamp | begin-rebase `load` about 10.5 → 6–7.5 ms with 1,000 commits |
| The history endpoint returned every change set (712 KB for 1,001) | sample API and UI | paged: `?offset=&limit=`, 50 by default, "Load more" in the UI | a page is 2.4–2.8 ms and 33–37 KB |
| Each quantity or cost change recomputed its level's whole total over all siblings | sample `DeepForm` reactions | a value change adjusts the total by the difference (the reaction's old and new value); only structural changes recount | patch every leaf's unit cost at depth 1: **792 → 101 µs**; patch every field 2.98 → 1.09 ms |
| A draft document stored a full copy of its base snapshot, written on every patch and read on every request | sample `AggregateDraftDocument` | a draft stores its changes and its base commit id (section 7) | draft document with 100 changes: 219 → 8 KB at 2000 fields; history run: patch 10.0 → 7.5 ms, commit 10.4 → 12.7 ms |

## 7. How drafts are stored

A draft document holds the draft's changes and the id of the commit they are based on. Its base snapshot is
the aggregate's own document while the draft is up to date. When a commit replaces that snapshot, the
replaced one is kept as a separate document, because another draft may still be based on it; it is deleted
by a later commit once no draft refers to it. So a single writer has at most one kept copy, and a draft left
behind keeps exactly the snapshot it needs until it rebases.

The trade: every patch and every draft read gets cheaper (the draft document is as small as its changes),
and every commit writes one more document. In the history run a patch went from 10.0 to 7.5 ms and a commit
from 10.4 to 12.7 ms; a save-every-three-edits cycle (open, patch, commit) costs about the same as before,
and anything with more than one patch per commit comes out ahead.

## 8. Hot spots that remain

- **Draft responses carry everything.** About 270 KB of data and change rows per response at 2000 fields,
  185 KB on the deep tree. A production client would ask for one section at a time and fetch changes on
  demand. This is now the largest part of most requests (`project`).
- **Validate re-evaluates every rule.** Fine at these sizes (0.23–0.26 ms); a very large form could validate
  the changed scope only.
- **Path strings are not shared between aggregates.** Every value is stored under its full canonical path,
  and every loaded aggregate gets its own copy of each (section 5): 28–30% of a flat form's memory, 48% of a
  deep one's. Interning the keys when a snapshot is read would share them across aggregates of a flat form
  (their keys repeat); on forms whose items have unique ids (GUIDs) whole keys rarely repeat, and only
  storing paths as shared segments would help.

## 9. The UI at 2000 fields

The editor renders one level at a time: section cards, then a section's 25 fields. So a 2000-field form
renders no more inputs than a 100-field one. Field edits are batched into one patch (on navigation,
"Save now", or every 10 s). Collections are paged 50 items at a time. Whether a field changed is checked
against a set built once per response, not by scanning every changed path per field.
