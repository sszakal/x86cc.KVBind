---
title: Nested nodes
sidebar_position: 3
---

# Nested nodes

A nested node models a nullable, polymorphic slot: at most one active subtype at a path.

```csharp
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

```csharp
builder.NestedNode(x => x.Party, nested =>
{
    nested.Bind<CompanyParty>("COMPANY", company => company.Field(x => x.CompanyName));
    nested.Bind<PersonParty>("PERSON", person => person.Field(x => x.FullName));
});
```

Each subtype is bound under a type token (`COMPANY`, `PERSON`), which is stored as `$type`. A nested node needs at least one `Bind<TSubtype>(...)` definition.

## Initializing and dropping

In code, assign the property; the generator implements the slot:

```csharp
agreement.Party = new CompanyParty();   // initializes the slot and applies CompanyParty's defaults
agreement.Party = null;                 // drops it
```

The model property is usually declared with a `private set` (a public setter raises warning `KVB008`) and changed through domain methods.

Clients use `INIT` and `DROP` patches:

```csharp
agreement.Patch(
    KVPatchOperation.Init("/Party", "COMPANY"),
    KVPatchOperation.Set("/Party/CompanyName", "Contoso Ltd."));
```

## Default type and required slots

- `DefaultType<TSubtype>()` (or `DefaultType("TOKEN")`) initializes the slot when the owning object is born. The subtype must be bound; this is checked when the definition is built.
- `Required()` on the nested node reports `required` at the slot while no subtype is initialized.

```csharp
builder.NestedNode(x => x.Licensor, party => party.Bind<Party>("PARTY", ...).Required());
```

## Recursive models

A nested node type can hold collections whose items reach that type again, so a tree can grow without limit. Build the recursive type with its own `KVBindBuilder<T>` and pass its `SelfReference` — the definition it is still building — wherever the type occurs again:

```csharp
var assembly = new KVBindBuilder<Assembly>();
assembly.Field(x => x.AssemblyCode);
assembly.Collection(x => x.Components, components => components
    .Item<Component>(component => component
        .NestedNode(x => x.Detail, detail => detail
            .Bind<Assembly>("ASSEMBLY", assembly.SelfReference)        // the tree recurses here
            .Bind<Purchased>("PURCHASED", p => p.Field(x => x.Price)))));
assembly.Build();

var builder = new KVBindBuilder<Product>();
builder.Collection(x => x.Components, components => components
    .Item<Component>(component => component
        .NestedNode(x => x.Detail, detail => detail
            .Bind<Assembly>("ASSEMBLY", assembly.SelfReference)
            .Bind<Purchased>("PURCHASED", p => p.Field(x => x.Price)))));
var definition = builder.Build();
```

The `deep` model in the [sample application](/samples) is a complete example.

Binding, patching, reactions, validation, rebase and the [schema](/schema) all handle recursive definitions.
