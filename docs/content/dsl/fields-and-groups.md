---
title: Fields and field groups
sidebar_position: 1
---

# Fields and field groups

The definition DSL describes schema, validation, patch behavior, collection item types, nested node variants and change reactions. Everything starts from a `KVBindBuilder<TRoot>`:

```csharp
var builder = new KVBindBuilder<Agreement>();
// ... declarations ...
var definition = builder.Build();
```

`Build()` checks the declarations (bound subtypes, valid rules, unique seed ids, …) and throws on anything invalid, so mistakes surface at startup rather than at the first request.

## Fields

```csharp
builder.Field(x => x.Title);
```

Field options configure defaults, value rules, validation profiles and patch access:

```csharp
builder.Field(x => x.Code, f => f
    .Default("MSA")
    .Required()
    .MinLength(3)
    .MaxLength(6)
    .Pattern("[A-Z]+-?[0-9]*"));

builder.Field(x => x.Years, f => f.Range(1, 10));
builder.Field(x => x.Status, f => f.PatchReadOnly());
```

The field's value type is the model property's type and is exposed as `KVFieldDefinition.ValueType`. Patches convert incoming values to it (see [value types](/patching/operations#value-types)).

Value rules declared on field options are always applied; see [Validation](/behavior/validation) for profile-specific rules.

### Allowed values

A field can restrict its value to a declared list, with labels and stable tokens. See [Allowed values](/dsl/allowed-values).

## Display metadata

Fields, field groups, collections and nested nodes all accept a display name and free-form annotations. They do not affect behavior; they are exposed on the definition (`KVDefinition.DisplayName`, `KVDefinition.Annotations`) and in the [schema](/schema) for a UI:

```csharp
builder.Field(x => x.Notes, f => f
    .DisplayName("Notes")
    .MaxLength(500)
    .Annotate("ui:control", "textarea"));

builder.Collection(x => x.LineItems, items => items
    .DisplayName("Line items")
    .Item<AgreementLineItem>(...));
```

When no `DisplayName(...)` is declared, the definition falls back to the attribute's `DisplayName`:

```csharp
[KVBind("Title", DisplayName = "Agreement title")]
public partial string? Title { get; set; }
```

## Field groups

A field group is a named sub-object that structures fields, both in code and in storage paths (`General/Code`).

```csharp
builder.FieldGroup(x => x.General, group =>
{
    group.Field(x => x.Code);
});
```

The model type derives from `KVFieldGroupNode` and is exposed as a get-only, non-partial property:

```csharp
[KVBind("General")]
public AgreementGeneral General { get; } = new();
```

Groups can nest other groups, collections and nested nodes.

`FieldGroup` takes up to three arguments: the property, a definition of the group's members, and the group's options (display name, `Resettable()`, `PatchReadOnly()`, `Inherited()`):

```csharp
builder.FieldGroup(
    x => x.Audit,
    audit => audit.Field(x => x.ApprovedBy),
    options => options.DisplayName("Audit").PatchReadOnly());
```

### Resettable groups

Mark a group `Resettable()` to let clients restore it to its defaults with a `RESET` patch:

```csharp
builder.FieldGroup(
    x => x.Settings,
    group => group.Field(x => x.Theme, f => f.Default("dark")),
    options => options.Resettable());
```

See [Defaults and reset](/dsl/defaults-and-reset).

## Reusable definitions

A group type, collection item type or nested subtype can carry its own definition by implementing `IKVNodeDefinition`, the same interface a [root model can use](/integration/definitions#definitions-declared-on-the-model). It is then declared without an inline lambda, and every model that uses the type shares one definition:

```csharp
public partial class Address : KVFieldGroupNode, IKVNodeDefinition
{
    [KVBind("Street")] public partial string? Street { get; set; }
    [KVBind("City")]   public partial string? City { get; set; }

    public static KVNodeDefinition Definition { get; } = Build();

    private static KVNodeDefinition Build()
    {
        var builder = new KVBindBuilder<Address>();
        builder.Field(x => x.Street);
        builder.Field(x => x.City, f => f.Required());
        return builder.Build();
    }
}

builder.FieldGroup(x => x.BillingAddress);                                   // group
builder.FieldGroup(x => x.ShippingAddress, options => options.DisplayName("Ship to"));
```

The same works for collection item types (`KVCollectionItemNode`) and nested subtypes (`KVNestedNode`) that implement `IKVNodeDefinition`:

```csharp
builder.Collection(x => x.Contacts);                                         // single self-describing item type
builder.Collection(x => x.Lines, lines => lines.Item<ProductLine>().Item<DiscountLine>("DISCOUNT"));
builder.NestedNode(x => x.Payer, payer => payer.Bind<CompanyPayer>().Bind<PersonPayer>("PERSON"));
```

Without an explicit token, item types and nested subtypes use the type name as their token (`Company`).
