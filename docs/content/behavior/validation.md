---
title: Validation
sidebar_position: 2
---

# Validation

Validation rules are attached to fields, groups, collections and validation profiles. Validation does not block edits: a draft may hold invalid data while a user works on it. It does block commits: `CreateCommit(...)` validates with the active profile and throws `KVChangeSetValidationException` when there are errors.

## Value rules

Value rules can be declared in three places and report the same error codes everywhere:

| Rule | Applies to | Error code |
| --- | --- | --- |
| `Required()` | fields, and nested node slots (no subtype initialized) | `required` |
| `MinLength(n)` / `MaxLength(n)` | string fields | `min_length` / `max_length` |
| `Pattern(regex)` | string fields; the whole value must match | `pattern` |
| `Min(v)` / `Max(v)` / `Range(min, max)` | numbers, dates and other comparable values; bounds are inclusive | `min` / `max` |

```csharp
// On the field: always applies. Exposed on KVFieldDefinition (MinLength, MaxLength, Pattern, Min, Max).
builder.Field(x => x.Code, f => f.Required().MinLength(3).MaxLength(6).Pattern("[A-Z]+-?[0-9]*"));
builder.Field(x => x.Years, f => f.Range(1, 10));
builder.NestedNode(x => x.Licensor, party => party.Bind<Party>("PARTY", ...).Required());

// Under a profile.
builder.Field(x => x.Share, f => f.Validation(profiles => profiles
    .For<FullValidationProfile>(rules => rules.Range(0m, 100m))));

// In a group rule, typically under a condition.
builder.Validation((KVGroupRuleBuilder<Contract> rules) => rules
    .When(a => a.Get(x => x.Kind) == "guaranteed", guaranteed => guaranteed
        .Required(x => x.Guarantor)              // nested node slot
        .Pattern(x => x.Reference, "REF[0-9]+")
        .Range(x => x.Discount, 0, 50)));
```

- Length, pattern and bound rules never report an unset value (`null` or an empty string); combine them with `Required()`.
- Numbers are compared by value whatever their stored type; other values are compared only with a bound of the same type.
- Invalid declarations — a length rule on a non-string field, an invalid regular expression, `Min` above `Max` — throw when the definition is built.

## Collection rules

Collection rules validate counts and aggregate values:

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

Aggregate sums skip unset values and parse numeric text with the invariant culture. A value that is not a number is reported as `aggregate_non_numeric` at the item field (for example `LineItems/<id>/Amount`), and the threshold check is skipped for that validation run.

## Validation profiles

Profiles let the same aggregate be validated more or less strictly depending on its state — for example lightly while drafting and fully before submission. A profile is a marker object, selected by the root:

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

public partial class Agreement : KVRootNode
{
    protected override KVValidationProfile GetValidationProfile()
    {
        return IsReadyForFullReview
            ? FullValidationProfile.Instance
            : QuickValidationProfile.Instance;
    }
}
```

## Running validation

Run validation explicitly, or from a patch result:

```csharp
var validation = agreement.Validate();

var patch = agreement.Patch(KVPatchOperation.Set("/Title", ""));
var patchValidation = patch.Validate();
```
