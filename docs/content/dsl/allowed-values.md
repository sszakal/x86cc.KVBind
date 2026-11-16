---
title: Allowed values
sidebar_position: 2
---

# Allowed values

A field can restrict its value to a declared list — a fixed set of codes, an enum subset, a set of numbers, or options with parameters. Allowed values are part of the definition, so a client can render them as a select, radio group or checkboxes from the [schema](/schema), and validation reports anything outside the list as `allowed_values`.

## Declaring allowed values

### Plain values

The value is its own token and label:

```csharp
classification.Field(x => x.RiskTier, f => f.AllowedValues("A", "B", "C"));
classification.Field(x => x.Territory, f => f.AllowedValues(1, 2, 3, 4, 5));
```

### Labelled values

`AllowedValue(value, id, label)` gives each value a stable **id** — its token — and a label for display:

```csharp
builder.Field(x => x.Priority, f => f
    .AllowedValue(Priority.High, "hi", "High")
    .AllowedValue(Priority.Low, "lo", "Low"));
```

The token is the wire and storage form: the field above stores `"lo"`, a patch sends `"lo"` (or the member name, see below), and the typed property still reads `Priority.Low`. Because the stored form is the token, enum members can be renamed or renumbered without touching data.

### From a list

When the values come from a list, select the token and label:

```csharp
builder.Field(x => x.Currency, f => f.AllowedValues(
    Currencies.All,
    tokenSelector: c => c.Code,
    labelSelector: c => c.DisplayName));
```

### Components with placeholders

An allowed-value **component** is an option that asks for extra input, described by a display template and typed placeholders:

```csharp
classification.Field(x => x.DeductibleOption, f => f
    .AllowedValue("standard", "standard", "Standard")
    .AllowedValueComponent("custom", "custom", "Custom amount", c => c
        .Template("Custom — {Amount:C} per occurrence")
        .Placeholder<decimal>("Amount", "Deductible Amount"))
    .AllowedValueComponent("split", "split", "Split", c => c
        .Template("Split — {Property:C} property, {Liability:C} liability")
        .Placeholder<decimal>("Property", "Property Deductible")
        .Placeholder<decimal>("Liability", "Liability Deductible", required: false)));
```

The field stores only the chosen id (`"custom"`). The template and placeholders are descriptive metadata for a UI — exposed on `KVFieldDefinition.AllowedValueOptions` and in the schema — and the application decides where the placeholder values are kept (typically in fields of their own next to the option).

## List fields

For a field holding several values (an array or list), declare the allowed **elements**; each element is checked against the list:

```csharp
builder.Field(x => x.Tags, f => f
    .AllowedElementValue<string>("critical", "critical", "Critical")
    .AllowedElementValue<string>("spare", "spare", "Spare"));

builder.Field(x => x.Regions, f => f.AllowedElementValues(
    Regions.All, tokenSelector: r => r.Code, labelSelector: r => r.Name));
```

`AllowedElementValueComponent<TElement>(...)` declares element components the same way. `KVFieldDefinition.AllowedValuesApplyToElements` tells a UI which kind it is.

## How a `SET` is resolved

On a single-value field, a patch value is resolved by the definition:

1. **As a token.** `"lo"` selects `Priority.Low`. A token wins even when it also looks like another value — `10` is the value whose token is 10, even if 10 is also an enum member's number.
2. **Otherwise as the field type.** `"Low"` (an enum member name) or `"2"` for an `int` field, converted losslessly.

A JSON array or object on a single-value field is rejected. What happens to an unknown value depends on the tokens:

| Tokens | Unknown value |
| --- | --- |
| text (`"A"`, `"hi"`) | stored in the draft, so validation can report `allowed_values` and the user can correct it |
| not text (numbers) | rejected; the patch throws |

## Reading allowed values

```csharp
var field = definition.FindField("Priority")!;

field.HasAllowedValues;                 // true
foreach (var option in field.AllowedValueOptions)
    Console.WriteLine($"{option.Id}: {option.Label} {option.Template}");
```

Each `KVAllowedValueInfo` has `Id`, `Label`, `Template` and `Placeholders` (`Name`, `Label`, `ValueType`, `IsRequired`).
