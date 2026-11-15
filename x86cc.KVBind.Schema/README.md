# x86cc.KVBind.Schema

A serializer-neutral description of a KVBind model definition, and the mapping that builds it from the
real `KVNodeDefinition` — so the model's shape is never duplicated by hand.

```csharp
var schema = KVSchemaMapper.Map<InsuranceClaim>(registry);   // build once, reuse
return Results.Ok(schema);                                   // or MessagePack, XML, ...
```

The model (`KVSchemaDocument`, `KVNodeSchema`, `KVFieldSchema`, ...) is made of plain classes: strings,
booleans, numbers, enums, lists and nested schema classes, with no serializer attributes and no
`object`-typed members. Values such as defaults, bounds and annotations are invariant text, read according
to the member's `KVSchemaValueKind` (`KVSchemaValues` holds the conversion rules).

It describes the structure (fields, groups, collections with their item types, nested nodes with their
types), labels, value kinds, defaults, always-applied constraints, allowed values (including components
with placeholders), read-only / inherited / resettable flags, collection count and aggregate rules, seeds,
allowed and custom operations, and scalar annotations. Validation profile and group rules are compiled code
and are not described.

Definitions can be recursive: a nested node type can hold collections whose items reach that type again
(`Bind<T>(token, builder.SelfReference)`), so the tree can grow without limit. The schema stays finite. The
first occurrence of such a type carries an `Id`; an occurrence inside itself carries a `Ref` to that id and an
empty `Node`. `KVSchemaReferences.Index(document)` and `KVSchemaReferences.NodeOf(type, index)` resolve
them, so a client can walk a path of any depth.
