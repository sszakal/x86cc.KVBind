# Repository Instructions

## Project Shape

- `x86cc.KVBind.Core` is the runtime library: snapshots, overlays, typed nodes, DSL definitions, patching, validation, collections, nested nodes, and change reactions.
- `x86cc.KVBind.SourceGenerator` is a Roslyn incremental generator targeting `netstandard2.0`; it emits accessors for `[KVBind]` partial properties and diagnostics `KVB001`-`KVB009` (see KVBind Conventions).
- `x86cc.KVBind.UnitTests` exercises both runtime and generator behavior; it references the generator both as a project and as an analyzer.

## Commands

- Build everything: `dotnet build x86cc.KVBind.slnx`
- Test everything: `dotnet test x86cc.KVBind.UnitTests/x86cc.KVBind.UnitTests.csproj`
- Build only runtime: `dotnet build x86cc.KVBind.Core/x86cc.KVBind.Core.csproj`
- Run one test class or method: `dotnet test x86cc.KVBind.UnitTests/x86cc.KVBind.UnitTests.csproj --filter FullyQualifiedName~SourceGeneratorSmokeTests`

## Toolchain Notes

- Core and tests target `net10.0`; the source generator targets `netstandard2.0`. Use a .NET 10 SDK.
- The test project sets `EmitCompilerGeneratedFiles=true`; generated analyzer output goes under `x86cc.KVBind.UnitTests/obj/Debug/net10.0/generated` and should not be committed.

## Testing Notes

- Tests use xUnit and AwesomeAssertions.
- Several tests use `Meziantou.Framework.InlineSnapshotTesting`; snapshot serialization is configured in `x86cc.KVBind.UnitTests/AssemblyInitializer.cs`.
- Prefer adding focused tests near the relevant area: `Core/Binding`, `Core/Drafts`, `Core/Patching`, `Core/Reactions`, `Core/Validation`, or `SourceGenerator`.

## KVBind Conventions

- `[KVBind]` canonical keys may only contain `A-Z`, `a-z`, `0-9`, and `_`; invalid keys are source-generator errors. Keys are case-sensitive everywhere (generator, definitions, storage, patch paths).
- Generator diagnostics: `KVB001` scalar/nested-node property not `partial`; `KVB002` missing key; `KVB003` duplicate key (checked across all partial declarations of a type); `KVB004` invalid key characters; `KVB005` (warning) `[KVBind]` on a non-`KVNode` type; `KVB006` field-group/collection property has a setter; `KVB007` field-group/collection property is `partial`; `KVB008` (warning) nested-node property has a public setter; `KVB009` model declared inside a non-`partial` containing type.
- Scalar and nested-node `[KVBind]` properties must be `partial`; field-group and `KVCollectionNode<T>` properties do not.
- Runtime schema is defined with `KVBindBuilder<T>`; model attributes alone do not declare fields, collections, nested-node types, validation, or reactions.
- Collections require at least one `Item<TItem>(...)` definition; nested nodes require at least one `Bind<TSubtype>(...)` definition.
- Collection item ids are strings (`A-Z`, `a-z`, `0-9`, `_`, `-`; GUIDs in "D" format qualify) and form the canonical path segment, e.g. `Animals/Snake`. Defaults apply once, when an object is born: `KVRootNode.CreateNew`, collection item creation (code or `ADD`), and `INIT`; binding an existing aggregate never applies defaults. `Seed<TItem>(id?, configure?)` declares predefined items created when the collection is born (still empty); the id defaults to the type token, and it cannot be combined with `Default(...)`.
- `node.Reset()` / `RESET <path>` wipes a node's data in place (keeping `$type`, id and position) and re-applies its defaults. Patch `RESET` targets collection items (subject to `AllowOperations`), initialized nested nodes, and field groups declared `Resettable()`; never fields or the root.
- `AllowOperations(...)` on a collection is an allowlist of structural patch operations (`ADD`, `REMOVE`, `MOVE`, `RESET`, and registered custom operations, which must be listed too). Unset = everything allowed. It only restricts patches (including `DISCARD` that would change membership/order), not the in-code collection API.
- `PatchReadOnly()` on a field, field group, collection or nested node rejects every patch operation targeting the member or anything beneath it (exposed as `KVDefinition.IsPatchReadOnly`). It also blocks `RESET` of an enclosing node and a `DISCARD` that would revert a draft change to the member; `ADD`/`REMOVE`/`INIT`/`DROP` of an enclosing object stay allowed. Like `AllowOperations`, it restricts patches only, not the in-code API.
- Patch paths are canonical paths; built-in operations are `SET`, `UNSET`, `ADD`, `REMOVE`, `MOVE`, `INIT`, `DROP`, `DISCARD`, and `RESET`, and custom collection operations cannot override those names.
