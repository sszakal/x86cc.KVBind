# KVBind

KVBind is a Git-inspired runtime for versioned business object graphs.

It lets you build large document-like aggregates as ordinary strongly typed C# classes while storing their state in a canonical versioned model. From that single representation, KVBind provides draft editing, replayable commits, diffing, patch APIs, validation, schema evolution, and runtime change notifications without requiring each application to implement them separately.

KVBind is particularly suited to systems where business documents evolve over time and every change matters:

- Contract management
- Land Registry
- Insurance policies and claims 
- Medical Records
- Regulatory filings 
- Government Registers
- Engineering Documents
- Rights and licencing
- Product lifecycle management

Typical characteristics:

* deeply nested object graphs
* long editing sessions
* draft workflows
* audit requirements
* concurrent editing
* partial updates
* evolving schemas

https://github.com/user-attachments/assets/dbbd4264-4d86-4db5-8c68-da4fd882f18d

## Mental Model

Think of each KVBind aggregate as a small Git-like repository for an object graph.

A root object has committed state, draft state, and a history of changes. You can edit through typed C# properties or patch operations, inspect what changed, commit the draft, discard parts of it, and replay commits to reconstruct state over time.

KVBind is also close to event sourcing for UI edits. It records user-intended changes over large forms or object graphs, while still maintaining an effective current projection for normal typed reads.

<img width="561" height="701" alt="KVBind_diagram1" src="https://github.com/user-attachments/assets/79624fc1-95b4-4408-9731-65563abe87cf" />

## Quick Example

Define your model as ordinary C# node types. Fields can be generated from partial `[KVBind]` properties, while collections and groups are regular runtime nodes.

```csharp
public partial class Agreement : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("General")]
    public AgreementGeneral General { get; } = new();

    [KVBind("LineItems")]
    public KVCollectionNode<AgreementLineItem> LineItems { get; } = new();

    [KVBind("Party")]
    public partial AgreementParty? Party { get; private set; }

    [KVBind("Summary")]
    public partial string? Summary { get; set; }

    public void RecalculateSummary(KVChangeContext<Agreement> context)
    {
        Summary = $"Changed: {context.ChangedPath}";
    }
}

public partial class AgreementGeneral : KVFieldGroupNode
{
    [KVBind("Code")]
    public partial string? Code { get; set; }
}

public partial class AgreementLineItem : KVCollectionItemNode
{
    [KVBind("Description")]
    public partial string? Description { get; set; }

    [KVBind("Amount")]
    public partial decimal Amount { get; set; }
}

public abstract partial class AgreementParty : KVNestedNode;

public partial class CompanyParty : AgreementParty
{
    [KVBind("CompanyName")]
    public partial string? CompanyName { get; set; }
}

public partial class PersonParty : AgreementParty
{
    [KVBind("FullName")]
    public partial string? FullName { get; set; }
}
```

Then define the schema and runtime behavior with the DSL:

```csharp
var builder = new KVBindBuilder<Agreement>();

builder.Field(x => x.Title, options =>
{
    options.Validation(profiles => profiles
        .For<FullValidationProfile>(rules => rules.Required().MaxLength(100)));
});

builder.Field(x => x.Summary);

builder.FieldGroup(x => x.General, group =>
{
    group.Field(x => x.Code);
});

builder.Collection(x => x.LineItems, collection =>
{
    collection.Item<AgreementLineItem>(item =>
    {
        item.Field(x => x.Description);
        item.Field(x => x.Amount);
    });

    collection.MinCount(1);
    collection.Validation(profiles => profiles
        .For<FullValidationProfile>(rules =>
            rules.AggregateSum<AgreementLineItem, decimal>(x => x.Amount)
                .LessThanOrEqual(10_000m)));
});

builder.NestedNode(x => x.Party, nested =>
{
    nested.Bind<CompanyParty>("COMPANY", company =>
    {
        company.Field(x => x.CompanyName);
    });

    nested.Bind<PersonParty>("PERSON", person =>
    {
        person.Field(x => x.FullName);
    });
});

builder.OnChange(
    path => path.Collection(x => x.LineItems).Field(x => x.Amount),
    x => x.RecalculateSummary);

var definition = builder.Build();
```

## Typed Runtime Access

Once a root is bound to a runtime model, you work with normal typed properties and collection APIs.

```csharp
var snapshot = new KVSnapshot();
var overlay = KVOverlay.Create(snapshot, user: "alice");
var model = KVModelRoot.Create(overlay, definition);
var agreement = KVRootNode.Create<Agreement>(model, definition);

agreement.Title = "Services Agreement";
agreement.General.Code = "MSA-2026";

var itemId = Guid.NewGuid();
var item = agreement.LineItems.Create(itemId);
item.Description = "Implementation services";
item.Amount = 120m;

var changes = agreement.GetAllChanges();
```

Direct property edits, patch operations, validation, change tracking, and commits all flow through the same overlay-backed runtime.

## Core Runtime Model

KVBind separates committed data, draft edits, replayable changes, and typed runtime access.

The runtime model maps to the Git-like workflow:

- `KVSnapshot` is the committed projection of canonical path/value data.
- `KVOverlay` is a user-owned draft over that projection, similar to uncommitted changes.
- `KVCommit` is a replayable immutable changeset produced from an overlay.
- `KVModelRoot` binds snapshot and overlay data to a runtime definition.
- `KVRootNode` is the typed aggregate root API.

A typical edit/commit flow:

```csharp
var snapshot = new KVSnapshot();
var overlay = KVOverlay.Create(snapshot, user: "alice");
var model = KVModelRoot.Create(overlay, definition);
var agreement = KVRootNode.Create<Agreement>(model, definition);

agreement.Title = "Updated agreement";

var draftChanges = agreement.GetAllChanges();
var commit = agreement.CreateCommit(DateTimeOffset.UtcNow);

snapshot.Apply(commit);
```

Applications own persistence of snapshots, overlays, and commits. KVBind provides the runtime behavior and data structures.

## Canonical Storage, Flexible Layout

KVBind stores values by canonical paths instead of serializing the current C# object graph shape. Typed nodes, field groups, sections, and UI layouts can evolve without automatically forcing persisted data migrations. When a change *does* need to transform stored data — backfills, path renames, removals, or nested-type swaps — [Schema Migrations](#schema-migrations) handle it.

## Definition DSL

The definition DSL describes schema, validation, patch behavior, collection item types, nested node variants, and change reactions.

### Fields

```csharp
builder.Field(x => x.Title);
```

### Field Groups

```csharp
builder.FieldGroup(x => x.General, group =>
{
    group.Field(x => x.Code);
});
```

Mark a group with `Resettable()` to let clients reset it to its defaults with a `RESET` patch (see Resetting a subgraph):

```csharp
builder.FieldGroup(x => x.Settings, group => group.Field(x => x.Theme, f => f.Default("dark")), options => options.Resettable());
```

### Collections

```csharp
builder.Collection(x => x.LineItems, collection =>
{
    collection.Item<AgreementLineItem>(item =>
    {
        item.Field(x => x.Description);
        item.Field(x => x.Amount);
    });
});
```

Every collection item has an immutable string id, which is also its canonical path segment (`LineItems/<id>`). Ids may contain `A-Z`, `a-z`, `0-9`, `_` and `-`, and are case-sensitive. `Create()` without an id generates a GUID; pass your own id when the item should have a stable, meaningful path.

```csharp
var generated = agreement.LineItems.Create();                // LineItems/3f2b…-…
var fromGuid  = agreement.LineItems.Create(Guid.NewGuid());
var named     = agreement.LineItems.Create("shipping");      // LineItems/shipping
```

In a polymorphic collection the id and the item type are independent: the id is the path segment, the stored `$type` token selects the subtype. `Get<TSubtype>(id)` returns the item typed (or `null` if absent, and throws if the stored item is a different subtype):

```csharp
var snake = zoo.Animals.Create<Snake>("Snake");              // Animals/Snake, $type = "Snake"
var python = zoo.Animals.Create<Snake>("Python");            // same type, different id
var lizard = zoo.Animals.Get<Lizard>("Lizard");
```

#### Predefined items (seeds)

`Seed<TItem>(...)` declares items every new aggregate starts with, under stable ids, so they are addressable by the same path in every document (for example `/Animals/Snake`). The id defaults to the item's type token:

```csharp
builder.Collection(x => x.Animals, animals => animals
    .Item<Snake>(item => item.Field(x => x.Name, f => f.Default("unnamed")))
    .Item<Lizard>(item => item.Field(x => x.Legs, f => f.Default(4)))
    .Seed<Snake>()                                  // Animals/Snake
    .Seed<Lizard>("Gecko", l => l.Legs = 3)         // Animals/Gecko, overrides the default
    .Seed<Snake>("Python"));                        // Animals/Python
```

Seeds are created when the collection is born — with a new aggregate, or with the new item that owns the collection — and never afterwards: reopening an aggregate does not re-seed it, and a seeded item that was removed stays removed. Each seeded item first gets its declared defaults, then the optional `configure` callback runs. Existing aggregates do not pick up seeds added to the schema later. Seed ids must be unique and valid, the seeded type must be declared with `Item<TItem>(...)`, and `Seed` cannot be combined with `Default(...)`. Declared seeds are available on `KVCollectionDefinition.Seeds`.

#### Restricting structural patch operations

`AllowOperations(...)` is an allowlist of the structural patch operations clients may apply to a collection — `ADD`, `REMOVE`, `MOVE`, `RESET` of an item, and custom operations registered on it (which must be listed too). Without it, everything is allowed; `AllowOperations()` with no arguments locks both membership and order:

```csharp
builder.Collection(x => x.Animals, animals => animals
    .Item<Snake>(...)
    .Seed<Snake>()
    .AllowOperations(KVPatchOperations.Move));      // fixed set of items, reorderable
```

Field patches inside the items (`SET /Animals/Snake/Name`) are unaffected. A `DISCARD` that would add, remove or reorder items of a restricted collection is rejected the same way. The restriction applies to patches only; the in-code collection API (`Create`, `RemoveById`, `MoveById`) used by application code and migrations stays available. `KVCollectionDefinition.AllowedOperations` (`null` = unrestricted) and `IsOperationAllowed(...)` let a UI hide actions that would be rejected.

### Defaults

Fields declare defaults with `f.Default(value)`, nested nodes with `DefaultType<TSubtype>()`, and collections with `Seed<TItem>(...)` or `Default(...)`. Defaults are applied once, when an object is born, and only fill values that are still unset:

- `KVRootNode.CreateNew(...)` applies defaults across the whole new aggregate.
- Creating a collection item — in code or with an `ADD` patch — applies that item's defaults, including its default nested types and seeded sub-collections.
- An `INIT` patch applies the defaults of the newly initialized nested node.

Binding an existing aggregate with `KVRootNode.Create(...)`, editing it, merging or rebasing never applies defaults, so previous changes are never overwritten.

### Resetting a subgraph

`node.Reset()` (or a `RESET` patch on the node's path) replaces a node's data with a freshly born object of the same type, in place: everything stored under the node is wiped, its `$type` is kept, and its defaults are applied again, including default nested types and seeded sub-collections (extra items in those sub-collections are dropped).

```csharp
zoo.Patch(KVPatchOperation.Reset("/Animals/Lizard"));   // same id, same position, fresh data
zoo.Patch(KVPatchOperation.Reset("/Owner"));            // same nested type, fresh data
zoo.Patch(KVPatchOperation.Reset("/Settings"));         // field group declared Resettable()
```

A reset collection item keeps its id and its position, and the change set reports field-level edits of the same item rather than a removal and an addition. In contrast, removing an item and adding one with the same id also yields fresh data but appends the item at the end.

Patch rules: a collection item can be reset unless its collection restricts operations with `AllowOperations(...)` without `RESET`; a nested node can be reset when it is initialized; a field group only when declared `Resettable()`; fields and the root cannot be reset (use `Clear()` to drop the root's draft changes). In code, `Reset()` is available on any non-root node.

### Nested Nodes

Nested nodes model a nullable polymorphic slot: one active subtype at a path.

```csharp
builder.NestedNode(x => x.Party, nested =>
{
    nested.Bind<CompanyParty>("COMPANY", company => company.Field(x => x.CompanyName));
    nested.Bind<PersonParty>("PERSON", person => person.Field(x => x.FullName));
});
```

Patch operations can initialize or drop the active subtype:

```csharp
agreement.Patch(
    KVPatchOperation.Init("/Party", "COMPANY"),
    KVPatchOperation.Set("/Party/CompanyName", "Contoso Ltd."));
```

### Change Reactions

Change reactions let definitions react to direct typed setters and patch mutations.

```csharp
builder.OnChange(
    path => path.Collection(x => x.LineItems).Field(x => x.Amount),
    x => x.RecalculateSummary);
```

The reaction method receives a context:

```csharp
public void RecalculateSummary(KVChangeContext<Agreement> context)
{
    Summary = $"Changed: {context.ChangedPath}";
}
```

Reactions bubble through field groups, collections, collection items, and nested nodes. Reaction execution state is scoped to the root aggregate, so separate edits start cleanly. KVBind detects active reaction cycles such as `A -> B -> A` and keeps a maximum chain-length guard for runaway non-repeating chains.

### Validation

Validation rules are attached to fields, groups, collections, and profiles.

```csharp
builder.Field(x => x.Title, options =>
{
    options.Validation(profiles => profiles
        .For<FullValidationProfile>(rules => rules.Required().MaxLength(100)));
});
```

Collection rules can validate count and aggregate values:

```csharp
builder.Collection(x => x.LineItems, collection =>
{
    collection.Item<AgreementLineItem>(item => item.Field(x => x.Amount));
    collection.MinCount(1);
    collection.Validation(profiles => profiles
        .For<FullValidationProfile>(rules =>
            rules.AggregateSum<AgreementLineItem, decimal>(x => x.Amount)
                .LessThanOrEqual(10_000m)));
});
```

Aggregate sums skip unset values and parse numeric text with the invariant culture. A value that is not a number is reported as `aggregate_non_numeric` at the item field (for example `LineItems/{id}/Amount`), and the threshold check is skipped for that validation run.

Validation profiles are marker objects selected by the root:

```csharp
public sealed record QuickValidationProfile : KVValidationProfile
{
    public static QuickValidationProfile Instance { get; } = new();
    private QuickValidationProfile() { }
}

public sealed record FullValidationProfile : KVValidationProfile
{
    public static FullValidationProfile Instance { get; } = new();
    private FullValidationProfile() { }
}

protected override KVValidationProfile GetValidationProfile()
{
    return IsReadyForFullReview
        ? FullValidationProfile.Instance
        : QuickValidationProfile.Instance;
}
```

Run validation explicitly or through patch results:

```csharp
var validation = agreement.Validate();

var patch = agreement.Patch(KVPatchOperation.Set("/Title", ""));
var patchValidation = patch.Validate();
```

## Patching

Patch operations address canonical paths and run sequentially in the order supplied. This allows one request to create a row and then set fields on that row.

```csharp
var itemId = Guid.NewGuid();

var result = agreement.Patch(
    KVPatchOperation.Add("/LineItems", new KVAddPatchPayload(itemId)),
    KVPatchOperation.Set($"/LineItems/{itemId:D}/Description", "Implementation services"),
    KVPatchOperation.Set($"/LineItems/{itemId:D}/Amount", 120m));

var validation = result.Validate();
```

Built-in operations:

- `SET` updates a field value.
- `UNSET` removes a field value from the draft.
- `ADD` creates a collection item with a client-provided id (any valid item id, or a GUID) and applies its defaults.
- `REMOVE` removes a collection item.
- `MOVE` reorders a collection item.
- `INIT` selects a nested node subtype.
- `DROP` clears a nested node slot.
- `DISCARD` discards draft changes at a path.
- `RESET` replaces a collection item, nested node or resettable field group with fresh data and its defaults, in place.

Custom collection operations can be registered on collection definitions:

```csharp
builder.Collection(x => x.LineItems, collection =>
{
    collection.Operation<GroupLineItems>("GROUP", x => x.GroupLineItems);
    collection.Item<AgreementLineItem>(item => item.Field(x => x.Amount));
});
```

Built-in operation names are reserved and cannot be overridden. A collection can restrict which structural operations clients may apply with `AllowOperations(...)` (see Collections).

## Schema Migrations

As a layout evolves, persisted data has to move with it. Because KVBind stores values by canonical path rather than the serialized C# shape, *additive* changes — a new optional field, a new collection, a reordered UI — need no migration at all; old data simply reads through. Migrations are for the changes that genuinely transform stored data: backfilling a value, removing a field, moving or renaming a path, or swapping a nested node's type.

A migration translates into a **migration commit** — the same replayable `KVCommit` as any edit — so a snapshot stays reconstructible from its commit chain. Migrations are registered on the root definition and each targets a schema version:

```csharp
builder.Migration(2, m => m
    .Backfill(KVTarget.Root.Seg("Status"), _ => "draft")   // new field, backfilled
    .Remove(KVTarget.Root.Seg("LegacyCode")));             // dropped field

builder.Migration(3, m => m
    .RenameSegment("General", "Header")                    // field-group key renamed
    .RenameField(KVTarget.Root.Seg("LineItems").AnyItem().Seg("Description"), "Details"));
```

### Targeting with `KVTarget`

`KVTarget` is a structural selector, not a regex. It resolves against the data, expanding collection rows as wildcards and filtering polymorphic instances by their stored `$type` discriminator — something a single-key pattern cannot express:

```csharp
// Every collection row (any item id):
KVTarget.Root.Seg("LineItems").AnyItem().Seg("Description")

// Only rows whose $type is a given token:
KVTarget.Root.Seg("LineItems").AnyItem(ofType: "DiscountLine").Seg("Amount")

// A field on one nested-node subtype only, leaving the other untouched:
KVTarget.Root.Seg("Party").OfType("PERSON").Seg("FullName")
```

Wildcard-aware renames carry whole subtrees, including ids and nested nodes under collection rows:

```csharp
m.RenameNode(KVTarget.Root.Seg("Party"), "Counterparty");   // rename a nested-node slot everywhere it occurs
```

### Replacing a nested node type

Swapping a nested node from one type to another (here `PERSON` → `COMPANY`) flips the `$type` discriminator and reshapes the fields — drop the old type's unique fields, add the new type's, leave common fields untouched. Fields are read from the pre-migration data, so a new field can be seeded from an old one:

```csharp
builder.Migration(4, m => m.ReplaceNestedType(
    KVTarget.Root.Seg("Party").OfType("PERSON"),
    toType: "COMPANY",
    reshape: r => r
        .Drop("FullName")                                          // PERSON-only field
        .Set("CompanyName", view => view.Sibling("FullName"))));   // COMPANY-only, seeded from the old value
```

### Applying migrations

```csharp
// Brings the snapshot up to the newest registered version — one chained commit per pending migration.
var commits = KVMigrator.Migrate(snapshot, definition);
```

Only migrations newer than the snapshot's `SchemaVersion` run, each as its own commit; an already-current snapshot does nothing. Applied migrations never re-run and the up-to-date check is O(1), so the list stays cheap no matter how long it grows.

### New aggregates

A brand-new aggregate is already in the newest layout, so mint its snapshot through the definition to stamp the current version — otherwise it defaults to `0` and would be mistaken for legacy data and needlessly re-migrated:

```csharp
var snapshot = definition.NewSnapshot();   // born stamped at CurrentSchemaVersion
```

### Background migration in batches

For large data sets, `KVMigrator.MigrateBatchAsync` runs an optional async **prepare** phase once per migration over only the subset of a batch that still needs it — so external lookups for a backfill are fetched in one round-trip instead of per aggregate:

```csharp
builder.Migration(5, m => m
    .Prepare((subset, ct) => LoadRatesAsync(subset.Select(s => s.Key), ct))   // one batch fetch
    .Backfill(KVTarget.Root.Seg("Rate"),
        view => view.Context.PreparedAs<IReadOnlyDictionary<object, decimal>>()[view.Context.Key!]));

var subjects = aggregates.Select(a => new KVMigrationSubject { Key = a.Id, Snapshot = a.Snapshot }).ToList();
var results = await KVMigrator.MigrateBatchAsync(subjects, definition, cancellationToken: ct);
```

Subjects already at the latest version contribute no commits and trigger no prepare, so re-running a partially completed migration over already-migrated data is a no-op.

## Source Generation

KVBind uses partial `[KVBind]` properties for typed accessor generation.

```csharp
public partial class Agreement : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }
}
```

The generated property implementation reads and writes through the bound KVBind runtime. The DSL defines schema and behavior; the generator keeps the model surface ergonomic.

## Current Status

KVBind is actively evolving toward a standalone package. The current runtime focuses on the core model: canonical field data, draft overlays, typed wrappers, patching, validation, nested nodes, collections, change reactions, and schema migrations.

APIs may change while the runtime is hardened.

## Build And Test

From a standalone KVBind repository:

```bash
dotnet build
dotnet test
```

When working directly with the KVBind projects:

```bash
dotnet build x86cc.KVBind.Core/x86cc.KVBind.Core.csproj
dotnet test x86cc.KVBind.UnitTests/x86cc.KVBind.UnitTests.csproj
```
