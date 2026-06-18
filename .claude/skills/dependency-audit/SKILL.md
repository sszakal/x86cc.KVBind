---
name: dependency-audit
description: Check KVBind dependencies for known vulnerabilities and pinning/supply-chain hygiene before packing or publishing NuGet packages. Use when reviewing dependencies, preparing a release, or asked about security of the dependency graph.
---

# Dependency / supply-chain audit

KVBind ships two NuGet packages (Core + SourceGenerator) and the integration tests pull external container images. The realistic security surface is the dependency graph and the pack/publish path — this skill covers both.

## Vulnerable packages

```bash
dotnet restore x86cc.KVBind.slnx
dotnet list x86cc.KVBind.slnx package --vulnerable --include-transitive
```

Also surface outdated and deprecated packages:

```bash
dotnet list x86cc.KVBind.slnx package --outdated
dotnet list x86cc.KVBind.slnx package --deprecated
```

Report findings by severity. For a transitive vulnerability, identify the top-level package pulling it in before proposing a bump.

## Pinning & source hygiene

- Check `nuget.config` for a locked, trusted package source (avoid implicitly trusting arbitrary feeds).
- Check `global.json` pins the SDK version (reproducible builds).
- Prefer central package management / explicit versions over floating (`*`) version ranges in `.csproj` files; flag any floating versions.

## Release path

- The `pack (validate)` CI step builds both packages on every push/PR — confirm it still passes before a release.
- `dotnet nuget push` is denied by `.claude/settings.json` for agents; publishing is a deliberate human action. Do not attempt to push packages.

## Reporting

Produce a short report: vulnerable/deprecated packages (with severity and the fix), any floating versions, and SDK/source pinning status. Recommend fixes; do not bump versions without surfacing the change first.
