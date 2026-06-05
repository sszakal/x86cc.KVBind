# KVBind

Typed C# bindings for canonical key-value document data.

KVBind is a typed C# runtime for editing large schema-driven object graphs as canonical key-value data. It gives each aggregate a Git-like editing model: committed snapshots, user draft overlays, replayable commits, patch operations, validation, and change reactions.

It is intended for large forms and document-like aggregates where users may spend time drafting changes before committing them, APIs need precise patch operations, and data identity must stay stable even when layout, sections, screens, or typed wrappers change.

KVBind gives you:

- draft editing over committed object state
- easy change detection and semantic diffs
- patch-based editing for API workflows
- replayable commits that can reconstruct an aggregate over time
- canonical storage decoupled from UI and typed model layout
- fewer migrations when forms, sections, or object wrappers change
- definition-driven validation and dependency behavior


- generated C# property accessors over a shared runtime


<img width="400" height="290" alt="kvbind_demo" src="https://github.com/user-attachments/assets/f607c5dd-8bb2-49cc-80ec-d091fda59ca9" />


## Mental Model

Think of each KVBind aggregate as a small Git-like repository for an object graph.

A root object has committed state, draft state, and a history of changes. You can edit through typed C# properties or patch operations, inspect what changed, commit the draft, discard parts of it, and replay commits to reconstruct state over time.

KVBind is also close to event sourcing for UI edits. It records user-intended changes over large forms or object graphs, while still maintaining an effective current projection for normal typed reads.

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

KVBind stores values by canonical paths instead of serializing the current C# object graph shape. Typed nodes, field groups, sections, and UI layouts can evolve without automatically forcing persisted data migrations.

If a field moves from one group, tab, section, or screen to another but keeps the same canonical key, the stored value can stay where it is. Migrations are still possible when business identity changes, but layout-only changes do not have to become data-shape changes.

This is useful for long-lived business documents where the form evolves over time but existing persisted aggregates must remain readable, editable, and diffable.

## Definition DSL

The definition DSL describes schema, validation, patch behavior, collection item types, nested node variants, and change reactions.

### Fields

```csharp
builder.Field(x => x.Title);
```

Fields are stable canonical values. Moving a field in your UI should not require moving the persisted value.

### Field Groups

```csharp
builder.FieldGroup(x => x.General, group =>
{
    group.Field(x => x.Code);
});
```

Field groups organize typed access without making the whole document depend on a single object serialization shape.

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

Collection rows use immutable GUID row identity. Typed code can create client- or server-provided rows:

```csharp
var item = agreement.LineItems.Create(Guid.NewGuid());
```

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
- `ADD` creates a collection item with a client-provided GUID.
- `REMOVE` removes a collection item.
- `MOVE` reorders a collection item.
- `INIT` selects a nested node subtype.
- `DROP` clears a nested node slot.
- `DISCARD` discards draft changes at a path.

Custom collection operations can be registered on collection definitions:

```csharp
builder.Collection(x => x.LineItems, collection =>
{
    collection.Operation<GroupLineItems>("GROUP", x => x.GroupLineItems);
    collection.Item<AgreementLineItem>(item => item.Field(x => x.Amount));
});
```

Built-in operation names are reserved and cannot be overridden.

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

KVBind is actively evolving toward a standalone package. The current runtime focuses on the core model: canonical field data, draft overlays, typed wrappers, patching, validation, nested nodes, collections, and change reactions.

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
