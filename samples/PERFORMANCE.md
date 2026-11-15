# KVBind performance on large forms

The same generated form at 100, 500, 1000 and 2000 fields (`large-{N}`; see [README.md](README.md)): 25-field
sections cycling through ten field kinds with their rules, plus 50 line items with a reaction-maintained
total. Measured on an AMD Ryzen 9 9950X with .NET 10.0.2 and Postgres 18 in a local container, in November 2026.

**In short:** KVBind's own work grows linearly with the size of the form and stays small. At 2000 fields a
request spends **under 0.1 ms** binding, **0.03 ms** patching one field, **0.25 ms** validating, and
**1.3 ms** applying 2000 field changes. The rest of a request is storage I/O (Marten/Postgres) and turning
the aggregate into JSON for the client.

## 1. In-process stages (BenchmarkDotNet)

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*LargeForm*"`. The aggregate is
committed with every field set plus 50 lines. "100 changes" means a draft with 100 changed fields. Mean
time per operation, with allocation in parentheses.

| Stage | 100 fields | 500 | 1000 | 2000 |
|---|---:|---:|---:|---:|
| bind (load → typed root) | 7.8 µs (28 KB) | 11.2 µs (41 KB) | 16.2 µs (61 KB) | 25.8 µs (98 KB) |
| bind + patch 1 field | 8.6 µs (30 KB) | 11.9 µs (42 KB) | 16.7 µs (63 KB) | 26.9 µs (100 KB) |
| bind + patch 100 fields | 67 µs (123 KB) | 72 µs (135 KB) | 75 µs (156 KB) | 88 µs (193 KB) |
| bind + patch every field | 68 µs (123 KB) | 316 µs (539 KB) | 637 µs (1.0 MB) | 1.30 ms (2.0 MB) |
| validate (100 changes) | 54 µs | 90 µs | 136 µs | 237 µs |
| compute deltas (100 changes) | 21 µs | 29 µs | 41 µs | 68 µs |
| commit (validate + build, 100 changes) | 55 µs | 91 µs | 138 µs | 240 µs |
| rebase with 100 conflicts | 33 µs | 44 µs | 54 µs | 78 µs |
| project all fields to JSON (sample) | 75 µs | 157 µs | 221 µs | 390 µs |

- Binding is cheap because it is lazy: values are read from the overlay when accessed. Patching costs
  about 0.65 µs per changed field, including type conversion, allowed values, `PatchReadOnly` and reactions.
- Validation is a full evaluation, so it grows with the size of the form, not with the number of changes.
- Rebase cost depends on the commits it folds in and on the number of conflicts, hardly on the form's size.

## 2. End to end over HTTP

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- load http://localhost:5101 5 report.md`
runs requests one after another against the sample. The API was a Debug build under Aspire, and Postgres was
local. Each round runs the full draft workflow for one aggregate. The table shows the p50 round trip, the API's
own total and the stages it reports in `Server-Timing` (the ⏱ panel in the UI shows the same per request).

| 2000 fields | p50 | server | biggest stages (p50 ms) | response |
|---|---:|---:|---|---:|
| create | 6.9 ms | 6.6 | persist 4.3, project 0.9, commit 0.5, bind 0.4 | 32 KB |
| patch all 2000 fields | 24.2 ms | 22.7 | project 9.6, persist 6.5, **patch 3.3**, load 1.2 | 231 KB |
| patch 1 field (after 2000 + 50 lines) | 27.8 ms | 26.0 | project 12.6, persist 6.8, load 5.5, **patch 0.02** | 271 KB |
| get draft | 20.7 ms | 18.9 | project 12.5, load 5.7, **bind 0.06** | 271 KB |
| validate | 7.2 ms | 7.0 | load 5.6, **validate 1.3** | — |
| commit | 30.0 ms | 29.7 | persist 13.2, project 9.1, load 5.3, **commit 1.1** | 52 KB |
| get snapshot | 6.8 ms | 6.5 | load 4.8, project 1.2 | 51 KB |

At 100 fields the same calls take 2–9 ms. The KVBind stages are in bold: together they are under 5% of any
request. The rest is:

- **persist:** a draft document holds its base snapshot and its changes, and Marten writes the whole document.
- **load:** reading those documents back. Each stored value carries its type name and is deserialized on
  its own.
- **project:** for a draft, the JSON tree of the data plus a labelled change row per changed path. The
  overlay drawer in the UI shows these rows; at 2000 changed fields that is about 2,350 rows.

## 3. Hot spots found, and what was done

| Found | Where | Change | Effect at 2000 fields |
|---|---|---|---|
| `Patch` computed the whole draft's deltas on every call, even when the caller never reads them | KVBind `KVRootNode.Patch` | `KVPatchResult.Changes` is now the patch's own change set, recorded from the change notifications as it runs (cost follows what the patch touched); the whole draft's changes are `GetAllChanges()` | patch 1 field **95.5 → 27.6 µs**, 381 → 100 KB; patch 100 fields 168 → 91 µs |
| Every stored value resolved its assembly-qualified type name again when read | KVBind `KVValueJsonConverter` | resolved types are cached by name | draft `load` stage 5.9 → about 4.3 ms |
| Change rows looked for a path's descendants by scanning every key, per change (O(changes × keys)) | sample `AggregateService` | field paths skip the check (the schema knows them); other paths use a sorted key index | draft `project` 14 → 5 ms after a full patch; commit `project` 14.6 → 9 ms |
| `get draft` p50 | (all of the above) | | 26.3 → 20.7 ms; at 1000 fields 15.6 → 11.0 ms |

Hot spots that remain (all in the sample, not in KVBind):

- **Draft responses carry everything.** About 270 KB of data and change rows per response at 2000 fields.
  A production client would ask for one section at a time and fetch changes on demand.
- **Draft documents store their base snapshot.** That makes persist and load proportional to the whole
  aggregate. Storing only the base commit id and reading the snapshot at that commit would keep a draft
  document as small as its changes.
- **Validate re-evaluates every rule.** Fine at these sizes (0.25 ms); a very large form could validate the
  changed scope only.

**Later change — per-patch change sets.** `Patch` now records the change set of each patch as it runs
(`KVPatchResult.Changes`), so the patch rows above cost slightly more than when they were measured: at 2000
fields, patch 1 field 29.5 µs and patch every field 1.58 ms (was 26.9 µs and 1.30 ms); on the deep form,
building the depth-4 tree 5.7 ms (was 3.7 ms). Still far below the 95 µs a one-field patch cost originally.

## 4. The UI at 2000 fields

The editor renders one level at a time: section cards, then a section's 25 fields. So a 2000-field form
renders no more inputs than a 100-field one. Field edits are batched into one patch (on navigation,
"Save now", or every 10 s). Collections are paged 50 items at a time. Whether a field changed is checked
against a set built once per response, not by scanning every changed path per field.

## 5. Deep, recursive forms

`deep` is a tree that can grow without limit: components (collection items) with their own `Measurements`
collection and an empty `Detail` slot; an `ASSEMBLY` in that slot holds components again, a `PURCHASED` part
ends the branch. Costs roll up through every level by change reactions (each assembly's `Subtotal`, the root's
`TotalCost`). The benchmark shapes have about 1,000 field values and the same 45 components (42 at depth 3)
at every depth, so only the depth changes: 45 flat (1,084 values), 15 × 3 (1,069), 6 × 7 (994) and 3 × 15
(1,063). Most values sit in the deepest level.

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*DeepForm*"`

| Stage | depth 1 | 2 | 3 | 4 |
|---|---:|---:|---:|---:|
| bind | 31.7 µs | 23.5 µs | 22.7 µs | 27.3 µs |
| project the whole tree to JSON (sample) | 201 µs | 204 µs | 197 µs | 227 µs |
| build the tree from empty (≈1,250 ADD / INIT / SET) ¹ | 3.14 ms | 2.99 ms | 2.68 ms | 3.72 ms |
| bind + patch 1 field at the deepest level | 32.9 µs | 26.1 µs | 25.9 µs | 33.1 µs |
| bind + patch 1 deepest price (recomputes every level above) | 51 µs | 62 µs | 47 µs | 55 µs |
| bind + patch every field ¹ | 2.98 ms | 2.09 ms | 2.44 ms | 2.86 ms |
| bind + drop the first top-level subtree ¹ | 64 µs | 70 µs | 52 µs | 61 µs |
| validate (100 changes spread over the tree) | 128 µs | 155 µs | 184 µs | 258 µs |
| compute deltas (100 changes) | 62 µs | 73 µs | 80 µs | 100 µs |
| commit (validate + build, 100 changes) | 126 µs | 156 µs | 186 µs | 263 µs |
| rebase with 100 conflicts | 95 µs | 115 µs | 130 µs | 152 µs |

¹ Re-measured after the two fixes below.

- **Depth costs little.** Binding, patching one field and projecting are flat from depth 1 to 4. Validate,
  deltas and commit grow by about 2× because a deeper tree of the same size has more nodes (each assembly
  is one more nested node with its own rules).
- **A change four levels down is cheap to propagate.** Setting the deepest price recomputes three assembly
  subtotals and the root total through cascading reactions in about 25 µs more than a plain write.
- **Patching every field is about 4× the flat 1,000-field form (0.64 ms)**, and the reactions are most of
  the difference: the 90 quantity and unit-cost writes each recompute their level's total over its siblings
  (about 17 µs each at depth 1, from "patch every leaf's unit cost": 45 writes, 0.79 ms). That is the
  sample's reaction design (recompute the sum), not KVBind's write path; an incremental reaction would use
  the context's old and new value.

Two hot spots in KVBind showed up here and were fixed:

| Found | Where | Change | Effect at depth 4 |
|---|---|---|---|
| Dropping (or re-initializing) a nested slot removed its keys one by one, and every removal rescanned all the draft's keys: quadratic in the subtree size | `KVNode.ClearNestedNodeModel` | one pass over the keys (`KVOverlay.ResetSubtree`, as `RESET` already does) | drop a 15-component subtree **2.2 ms / 37.5 MB → 61 µs / 235 KB** |
| Every write checks each reaction's path pattern at every ancestor, and each check split the path into a new array | `KVPathPattern.Matches` | compares segments in place, no allocation | patch every field 4.31 → 2.86 ms, 10.1 → 4.0 MB; build the tree 5.08 → 3.72 ms, 19.3 → 10.5 MB |

## 6. A long history on the deep form

`dotnet run -c Release --project x86cc.KVBind.Benchmarks -- history http://localhost:5101 1000 3 report.md`
builds the depth-4 tree through the API, a few edits per commit (open draft → patch 3 operations → commit),
then keeps re-editing every field until the aggregate has 1,000 commits; the tree is complete after 384.
Meanwhile four other users open drafts that end up 1, 10, 100 and 1,000 commits behind; each edits fields
the later commits change too and adds a component of its own. At the end they rebase, resolve, and commit.

Each commit as the history grows (p50 ms, round trip):

| Commits | open draft | patch | commit | commit's biggest stages |
|---|---:|---:|---:|---|
| 1–10 | 4.8 | 5.1 | 5.1 | persist 4.0, load 0.5 |
| 11–100 | 5.3 | 6.1 | 6.2 | persist 3.9, load 1.4 |
| 101–500 | 7.6 | 9.9 | 10.3 | persist 4.9, load 3.3, commit 0.7 |
| 501–1,000 | 7.6 | 9.9 | 10.3 | persist 5.0, load 3.3, commit 0.7 |

**The history's length costs nothing per commit**: commits 501–1,000 cost the same as 101–500. The growth
from 5 to 10 ms follows the tree filling up (load and persist carry the whole aggregate), and it stops once
the tree is complete.

Rebasing the drafts left behind, over HTTP:

| Behind by | reviewed | conflicts (derived, auto-resolved) | begin rebase | resolve one | finish | commit |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 7 | 7 (4) | 24 ms | 16 ms | 15 ms | 11 ms |
| 10 | 32 | 18 (4) | 14 ms | 12 ms | 14 ms | 13 ms |
| 100 | 259 | 11 (2) | 17 ms | 15 ms | 14 ms | 11 ms |
| 1,000 | 191 | 2 (2) | 34 ms | — | 12 ms | 12 ms |

The four drafts' components all survive the merges (the root ends with 7 top-level components); every
derived subtotal and total that both sides rewrote is resolved by the service and recomputed on finish.

The same in process (`--filter "*DeepHistory*"`): a draft branched off the complete tree with 20
conflicting edits and its own component, rebased over `Behind` commits of 3 edits.

| Behind by | begin rebase | begin + resolve + finish + recompute + commit | replay the commits onto the base |
|---:|---:|---:|---:|
| 1 | 119 µs (342 KB) | 0.90 ms (0.9 MB) | 25 µs |
| 10 | 129 µs (356 KB) | 0.94 ms (1.0 MB) | 26 µs |
| 100 | 230 µs (495 KB) | 1.10 ms (2.6 MB) | 33 µs |
| 1,000 | 623 µs (782 KB) | 1.77 ms (5.6 MB) | 109 µs |

Folding 1,000 commits (3,000 changes) into a rebase takes 0.6 ms; most of an end-to-end rebase is the
recompute (binding and `NotifyReplaced` over the whole tree) and the commit's validation, which do not depend
on how far behind the draft was.

Found in the sample and fixed: **rebase read the whole history.** To find the commits since the draft's base
it loaded every change set of the aggregate, so a draft one commit behind paid for all 1,000. It now reads
only the change sets since the base's timestamp (falling back to the whole history if the chain does not
reach the base): the load stage of begin-rebase went from about 10.5 to 6–7.5 ms for drafts 1 to 100 behind;
the rest is the draft and snapshot documents every request reads.

Still open: **the history endpoint returns every change set** (1,001 change sets, 712 KB, 8 ms). A client
showing history needs paging.
