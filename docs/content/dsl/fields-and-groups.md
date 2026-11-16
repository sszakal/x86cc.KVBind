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

### Allowed values and annotations

Fields can restrict their values to a list of allowed values — plain (`AllowedValues("low", "high")`) or labelled, each with a value, an id and a label — and carry metadata for a UI:

```csharp
component.Field(x => x.Category, f => f
    .DisplayName("Category")
    .AllowedValue("mechanical", "mechanical", "Mechanical")
    .AllowedValue("electrical", "electrical", "Electrical"));

component.Field(x => x.Tags, f => f
    .AllowedElementValue<string>("critical", "critical", "Critical")   // element-wise, for list fields
    .AllowedElementValue<string>("spare", "spare", "Spare"));

component.Field(x => x.Notes, f => f.MaxLength(500).Annotate("ui:control", "textarea"));
```

Tokens are the wire form a patch sends; see [Patch operations](/patching/operations#value-types) for how a `SET` resolves them. A value outside the list is reported by validation as `allowed_values`. Display names, allowed values and annotations are all part of the [schema](/schema).

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

### Resettable groups

Mark a group `Resettable()` to let clients restore it to its defaults with a `RESET` patch:

```csharp
builder.FieldGroup(
    x => x.Settings,
    group => group.Field(x => x.Theme, f => f.Default("dark")),
    options => options.Resettable());
```

See [Defaults and reset](/dsl/defaults-and-reset).
