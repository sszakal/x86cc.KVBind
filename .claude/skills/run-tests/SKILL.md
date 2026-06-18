---
name: run-tests
description: Run KVBind tests the correct way. Use whenever the user asks to run, check, or verify tests, or after changing runtime/generator code. Encodes the exact dotnet commands and avoids building the Aspire AppHost.
---

# Running KVBind tests

Always target the test projects directly. Never run `dotnet test` on the solution or the Aspire AppHost — the AppHost needs a workload and is not what you want to validate.

## Unit tests (default)

```bash
dotnet test x86cc.KVBind.UnitTests/x86cc.KVBind.UnitTests.csproj
```

This transitively builds Core + the source generator, so it is the right first command for almost any change.

## A single class or method

```bash
dotnet test x86cc.KVBind.UnitTests/x86cc.KVBind.UnitTests.csproj --filter FullyQualifiedName~SourceGeneratorSmokeTests
```

Use the focused filter while iterating, then run the full unit suite before declaring done.

## Build only the runtime

```bash
dotnet build x86cc.KVBind.Core/x86cc.KVBind.Core.csproj
```

## Integration tests (opt-in — needs Docker)

The integration suite spins up Postgres via Testcontainers and pulls images. Only run it when explicitly asked or when changing persistence/serialization:

```bash
dotnet test x86cc.KVBind.IntegrationTests/x86cc.KVBind.IntegrationTests.csproj
```

## Rules

- Report pass/fail with the real output. Never claim green without running.
- Generated analyzer output under `obj/.../generated` is not committed.
- Tests use xUnit + AwesomeAssertions; some use Meziantou InlineSnapshotTesting (see the `update-snapshots` skill if snapshots fail).
