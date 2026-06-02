# Repository Instructions

## Project Shape

- `x86cc.KVBind.Core` is the runtime library: snapshots, overlays, typed nodes, DSL definitions, patching, validation, collections, nested nodes, and change reactions.
- `x86cc.KVBind.SourceGenerator` is a Roslyn incremental generator targeting `netstandard2.0`; it emits accessors for `[KVBind]` partial properties and diagnostics `KVB001`-`KVB004`.
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

- `[KVBind]` canonical keys may only contain `A-Z`, `a-z`, `0-9`, and `_`; invalid keys are source-generator errors.
- Scalar and nested-node `[KVBind]` properties must be `partial`; field-group and `KVCollectionNode<T>` properties do not.
- Runtime schema is defined with `KVBindBuilder<T>`; model attributes alone do not declare fields, collections, nested-node types, validation, or reactions.
- Collections require at least one `Item<TItem>(...)` definition; nested nodes require at least one `Bind<TSubtype>(...)` definition.
- Patch paths are canonical paths; built-in operations are `SET`, `UNSET`, `ADD`, `REMOVE`, `MOVE`, `INIT`, `DROP`, and `DISCARD`, and custom collection operations cannot override those names.
