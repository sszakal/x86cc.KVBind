---
name: add-kvbind-feature
description: Playbook for adding a field, field group, collection, or nested node to a KVBind model. Use when adding new model surface so the [KVBind] attribute, builder wiring, conventions, and tests are all applied consistently.
---

# Adding KVBind model surface

A KVBind feature is never just a C# property — model attributes alone declare nothing. Every addition is: **declare the property → wire it in the builder → (optionally) add validation/reactions/migration → test.**

## 1. Declare the property

```csharp
[KVBind("CanonicalKey")]
public partial string? Title { get; set; }
```

Rules (enforced by the source generator — getting these wrong yields `KVB001`–`KVB004`):

- Canonical keys may contain only `A-Z`, `a-z`, `0-9`, `_`.
- **Scalar** and **nested-node** properties MUST be `partial`.
- **Field-group** (`KVFieldGroupNode`) and **`KVCollectionNode<T>`** properties must NOT be `partial` — initialize them inline (`= new();`).

## 2. Wire it in the builder

The runtime schema lives in `KVBindBuilder<T>`. Nothing exists until it is declared there.

```csharp
builder.Field(x => x.Title);
builder.FieldGroup(x => x.General, g => g.Field(x => x.Code));
builder.Collection(x => x.LineItems, c => c.Item<AgreementLineItem>(i => i.Field(x => x.Amount)));
builder.NestedNode(x => x.Party, n =>
{
    n.Bind<CompanyParty>("COMPANY", b => b.Field(x => x.CompanyName));
    n.Bind<PersonParty>("PERSON", b => b.Field(x => x.FullName));
});
```

Invariants:
- A collection requires at least one `Item<TItem>(...)`.
- A nested node requires at least one `Bind<TSubtype>(...)` with a string discriminator token.
- Collection rows use client-provided GUID identity (`collection.Create(Guid.NewGuid())`).

## 3. Optional behavior

- **Validation:** `options.Validation(p => p.For<FullValidationProfile>(r => r.Required().MaxLength(100)))`.
- **Change reactions:** `builder.OnChange(path => path.Collection(x => x.LineItems).Field(x => x.Amount), x => x.Method)`.
- **New persisted field that needs to move existing data:** see the `author-migration` skill. Additive optional fields need NO migration — old data reads through.

## 4. Test

Add focused tests under the matching `Core/...` folder using `KVModelTestBase` fixtures, then run the `run-tests` skill. Cover null/empty/edge cases — correctness is the priority here.
