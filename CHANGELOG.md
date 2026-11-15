# Changelog

Changes to the published packages: `x86cc.KVBind.Core`, `x86cc.KVBind.SourceGenerator` and
`x86cc.KVBind.Schema`. The samples, benchmarks and tests are not published; their changes are summarized at
the end.

## Unreleased

Suggested version: **0.2.0**. It contains behavior changes that callers can notice (listed first).

### Breaking and behavior changes

- **`KVRootNode.Patch(...)` returns the change set of that patch only.** `KVPatchResult.Changes` now lists what
  the patch itself changed: its operations and the writes of the change reactions they triggered, recorded
  while it runs and fixed when it returns. Before, it was every change the draft held against its snapshot
  (earlier patches and earlier sessions included), and on this branch's intermediate builds it was computed
  on first read. **Use `root.GetAllChanges()` for everything pending in the draft.**
  - A path the patch added, removed or replaced (`RESET`, `DISCARD`) stands for everything beneath it; an item
    added and removed again in the same patch is not reported; a field written back to its old value is no
    change (sequences are compared by content).
  - New: `KVChangeDelta.OldValue` and `NewValue` carry a field's value before and after the patch (null in
    `GetAllChanges()`).
- **`SET` on a field with allowed values reads the value the way a client sends it.** A JSON request carries
  tokens and typed values alike as text and numbers, so the field's definition now decides what a value means:
  - a token first: tokens are the wire form, so `10` means the value whose token is 10, even when 10 is also an
    enum member's number or another allowed value;
  - else the value converted to the field's type, losslessly: `"2"` or `2.0` for an `int`, a member name for an
    enum (`2.5` for an `int` is rejected);
  - a JSON array or object on a single-value field is rejected ("takes a single value");
  - an unknown value is kept in the draft for validation to report (`allowed_values`) only when the tokens are
    text; otherwise it is rejected. Before, such a value was stored and the typed property threw when read.
- **`DISCARD` of a collection item also reverts its membership.** An item the draft added disappears; an item
  it removed comes back at its committed position (after its nearest committed predecessor that is still
  present, else first); an item it only edited keeps its place and gets its committed values back. The revert
  is subject to `AllowOperations` (it needs `REMOVE` or `ADD` respectively). The same applies to
  `KVOverlay.Discard(path)`. Before, discarding an added item left it in the collection, empty.

### Fixed

- `RESET` on a model with a recursive definition (a nested type whose items hold that type again) no longer
  overflows the stack: the check for `PatchReadOnly` members visits each definition once.
- Rebase: when upstream switched the type of nested nodes at several levels and the draft edited inside them,
  one structural conflict is raised at the outermost switched node, whatever order the upstream commit lists
  its keys in. Before, overlapping conflicts could be raised, one per level.
- Rebase: resolving a structural conflict as Ours no longer writes base values back beneath an item the draft
  deleted.
- Values stored with enums as names (e.g. Marten with `EnumStorage.AsString`) are read back; before, reading
  such a value threw.
- A JSON `null` in a `SET` clears the field.
- The value-type name cache keeps only names that resolve, so a type whose assembly loads later still resolves,
  and it is capped (1,024 names), so unknown names in incoming documents cannot grow it.

### Performance

Measured on the sample's generated forms; see `samples/PERFORMANCE.md`.

- `Patch` no longer computes the whole draft's deltas on every call: a one-field patch on a 2,000-field form
  95.5 → 27 µs, 381 → 100 KB allocated.
- Dropping or re-initializing a nested slot is one pass over the keys instead of a removal per key that
  rescanned all keys: dropping a 15-component subtree 2.2 ms / 37.5 MB → 61 µs / 235 KB.
- Matching a change against reaction path patterns no longer allocates: patching every field of a four-level
  tree 4.3 → 2.9 ms, 10 → 4 MB.
- Stored value type names are resolved once per name instead of once per value.
- Reading a list field with allowed values no longer builds a set of the allowed elements on every read.

### Added

- **`x86cc.KVBind.Schema`** (new package): a serializer-neutral description of a model built from its real
  definition with `KVSchemaMapper.Map(...)`: structure, labels, value kinds, defaults, constraints, allowed
  values, read-only / inherited / resettable flags, collection rules, seeds, operations and scalar annotations.
  Plain classes, no serializer attributes, so it can be sent as JSON, MessagePack or XML.
  - Recursive definitions map to a finite schema: the first occurrence of a type that occurs inside itself
    carries an `Id`, the inner occurrence a `Ref`; `KVSchemaReferences.Index` and `NodeOf` resolve them.
- Recursive models: a nested node type can hold collections whose items reach that type again
  (`Bind<T>(token, builder.SelfReference)`), so a tree can grow without limit; binding, patching, reactions,
  validation, rebase and the schema all handle it.

### Also on main since 0.1.0

Features that landed before this release branch (see `AGENTS.md` for the rules each one follows):
`RESET` of collection items, nested nodes and resettable field groups; `PatchReadOnly()`; `KVChangeKind` for
change reactions (value and structural changes); custom patch operations on nodes and collections
(`Operation(...)`); `SET` value conversion to the field's declared type, rejecting enum numbers that are not
members; domain events (`Raise`, `DequeueEvents`); defaults applied when a nested node is assigned; value
constraints (`MinLength`, `MaxLength`, `Pattern`, `Min`, `Max`, `Range`) on fields, profiles and group rules;
aggregate validation that skips unset values; migrations; inheritance from a parent aggregate; rebase fixes.

### Not published (samples, tools, tests)

- One generic sample API and UI for every model, with `Server-Timing` stage timings; models for claims,
  underwriting (every feature), generated large forms (100–2,000 fields) and a recursive deep form.
- The sample stores drafts as their changes plus a base commit id (no embedded base snapshot), pages the
  history endpoint, answers only request errors with 400, and reads only the change sets a rebase needs.
- Benchmarks and tools: BenchmarkDotNet stages for large and deep forms and rebase over long histories;
  HTTP `load`, `history` and fixed-rate `rate` runs; `memory` and `capacity` measurements.
- The solution builds without warnings; package upgrades clear the NuGet security advisories (Marten 9.59.0,
  Aspire 13.6.5, Testcontainers 4.16.0, MessagePack 2.5.307).
