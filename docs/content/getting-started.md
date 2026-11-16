---
title: Getting started
sidebar_position: 2
---

# Getting started

## Install

KVBind targets .NET 10. Reference the runtime and the source generator:

```bash
dotnet add package x86cc.KVBind.Core
dotnet add package x86cc.KVBind.SourceGenerator
```

Add `x86cc.KVBind.Schema` as well if a client needs a description of the model (see [Schema package](/schema)).

## 1. Define the model

Models are ordinary C# classes deriving from KVBind node types. Scalar fields and nested nodes are `partial` properties marked with `[KVBind("Key")]`; the generator implements them. Field groups and collections are plain get-only properties.

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

## 2. Declare the schema

Attributes alone do not declare anything: the schema, validation and behavior are described with `KVBindBuilder<T>`.

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
    nested.Bind<CompanyParty>("COMPANY", company => company.Field(x => x.CompanyName));
    nested.Bind<PersonParty>("PERSON", person => person.Field(x => x.FullName));
});

builder.OnChange(
    path => path.Collection(x => x.LineItems).Field(x => x.Amount),
    x => x.RecalculateSummary);

var definition = builder.Build();
```

Build the definition once and reuse it.

## 3. Edit, inspect, commit

```csharp
var snapshot = definition.NewSnapshot();
var overlay = KVOverlay.Create(snapshot, user: "alice");
var agreement = KVRootNode.CreateNew<Agreement>(overlay, definition);

agreement.Title = "Services Agreement";
agreement.General.Code = "MSA-2026";

var item = agreement.LineItems.Create();
item.Description = "Implementation services";
item.Amount = 120m;

var changes = agreement.GetAllChanges();     // everything the draft holds against the snapshot
var validation = agreement.Validate();

var commit = agreement.CreateCommit(DateTimeOffset.UtcNow);
snapshot.Apply(commit);
```

Typed setters, patch operations, validation, change tracking and commits all flow through the same overlay-backed runtime. Your application owns persistence of snapshots, overlays and commits; KVBind provides the behavior and data structures.

## 4. Accept patches from a client

```csharp
var itemId = Guid.NewGuid();

var result = agreement.Patch(
    KVPatchOperation.Add("/LineItems", new KVAddPatchPayload(itemId)),
    KVPatchOperation.Set($"/LineItems/{itemId:D}/Description", "Implementation services"),
    KVPatchOperation.Set($"/LineItems/{itemId:D}/Amount", 120m),
    KVPatchOperation.Init("/Party", "COMPANY"),
    KVPatchOperation.Set("/Party/CompanyName", "Contoso Ltd."));

var patchValidation = result.Validate();
```

## Next steps

- Understand how snapshots, overlays and commits relate in the [mental model](/concepts/mental-model).
- Browse the [Definition DSL](/dsl/fields-and-groups) for everything a schema can declare.
- Run the [sample application](/samples) to see a generic API and UI driven by definitions.
