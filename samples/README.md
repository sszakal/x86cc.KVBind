# KVBind sample

An Aspire app (API + Postgres via Marten + Angular UI) that serves several KVBind models through **one
generic API and one generic UI**: nothing in either is written per model. Each model's form, labels, options,
rules and allowed operations come from its KVBind definition, via the `x86cc.KVBind.Schema` package.

```bash
ASPIRE_ALLOW_UNSECURED_TRANSPORT=true dotnet run --project samples/x86cc.KVBind.Sample.AppHost --launch-profile http
```

| Resource | URL |
|---|---|
| UI | http://localhost:5200 |
| API (Swagger) | http://localhost:5101/swagger |
| Aspire dashboard (traces show each request's KVBind stages) | http://localhost:15016 |

For UI work, `npx ng serve` in `x86cc.KVBind.Sample.UI` serves it on port 4200 against the same API.

## Models

| Route name | What it shows |
|---|---|
| `claims` | The simple example: an insurance claim with a policy group, damaged items, notes and a polymorphic claimant. Also served at `/api/claims` for older clients. |
| `underwriting` | **Every KVBind feature in one form** (see the map below). |
| `large-100` … `large-2000` | Generated forms of 100 / 500 / 1000 / 2000 fields for the performance demo. |
| `deep` | A recursive tree: components (collection items) with their own measurements and an empty `Detail` slot; an `ASSEMBLY` there holds components again, so the tree can grow without limit. Costs roll up through every level. For the depth part of the performance demo. |

A model is registered in `SampleSetup.cs` with an `AggregateModel<TRoot>`: its route name, create and list
fields, the validation-profile name, values computed in code, and (optionally) a legacy-data sample for
migrations, child support for inheritance, and its reaction-derived paths.

## Generic API — `/api/models/{model}/...`

| Method & path | |
|---|---|
| `GET /api/models` | the catalog |
| `GET {model}/definition` | the schema generated from the definition (`KVSchemaMapper`) |
| `POST {model}` · `GET {model}` | create (with first values, optionally `parentId`) · list with summary fields |
| `GET {model}/{id}/snapshot` · `/changesets` | committed data as a JSON tree · history with changes and domain events |
| `POST {model}/{id}/drafts` · `GET …/drafts/{draftId}` | open / resume a user's draft · read it |
| `POST …/patch` | KVBind patch operations, built-in or custom (`SUBMIT`, `APPROVE`, …) |
| `POST …/validate` · `/reset` · `/commit` | validate with the active profile · discard all · commit (409 stale, 422 invalid) |
| `POST …/rebase` · `/rebase/resolve` · `/rebase/finish` · `DELETE …/rebase` | rebase onto the latest commit |
| `POST {model}/legacy` · `POST {model}/migrate` | store an old-layout aggregate · migrate every stored aggregate in one batch |

Every response carries a `Server-Timing` header with the KVBind stages of the request (load, migrate, bind,
patch, react, validate, commit, project, persist). The UI's ⏱ panel shows them per call, and the same stages
are OpenTelemetry spans in the Aspire dashboard.

## Underwriting: one section per feature

The scenario tests in `x86cc.KVBind.IntegrationTests/Underwriting` follow the same map.

| | Feature | Where in the model | Tests |
|---|---|---|---|
| 1a | Scalar types and defaults | `Basics`: string, int, decimal, bool, `DateOnly`, `DateTimeOffset`, `TimeOnly`, `TimeSpan`, `Guid`, enum, `[Flags]` enum; defaults on create only | `FieldScenarioTests` |
| 1b | Allowed values | `Classification`: labelled, plain, numeric, components with placeholders, element-wise; unknown tokens kept for validation | `FieldScenarioTests` |
| 1c | Value rules | `Contact`: `Required`, length, `Pattern`, `Min`/`Max`/`Range`, profile-only rules | `FieldScenarioTests` |
| 1d | Profiles and group rules | submit profile once `Status` leaves draft; `When` rules (marine needs a vessel, a "yes" answer needs details) | `StructureScenarioTests` |
| 1e | Field groups | `Premises`: resettable, defaults re-applied, nested `Protection` group | `StructureScenarioTests` |
| 1f | Collections | `Locations` (BUILDING / VEHICLE, tenants inside buildings, count and aggregate rules), seeded `Questions` | `StructureScenarioTests` |
| 1g | `AllowOperations` / `PatchReadOnly` | locked `Questions`, `Contacts` without MOVE, read-only `Status`, `Rating`, `Endorsements` | `StructureScenarioTests` |
| 1h | Custom operations | `SUBMIT` / `QUOTE {baseRate, modifier}` / `BIND` / `DECLINE "reason"` on the root, `APPROVE` on a location, `ADD_ENDORSEMENT` on a collection | `BehaviourScenarioTests` |
| 1i | Nested nodes | `Insured` (PERSON / COMPANY, default COMPANY, required), `Vessel` | `BehaviourScenarioTests` |
| 1j | Change reactions | totals follow Value / Added / Removed / Moved / Replaced changes, from patches and code | `BehaviourScenarioTests` |
| 1k | Domain events | raised by operations, pending on the draft, published with the commit | `BehaviourScenarioTests`, `ApiScenarioTests` |
| 1l | Drafts, rebase, concurrency | every conflict kind, Ours/Theirs/Custom, stale commit, concurrent saves | `ApiScenarioTests` |
| 1m | Migrations | v1 rename + backfill, v2 replace nested type, v3 remove + backfill; on read and in batch | `ApiScenarioTests` |
| 1n | Inheritance | `Program` terms inherited from a parent application, read-only in the child, parent updates flow through | `ApiScenarioTests` |

## Large forms and performance

`samples/tools/GenerateLargeForms.cs` generates the large-form models (run it with
`dotnet run samples/tools/GenerateLargeForms.cs`; the output in `x86cc.KVBind.Sample.Api/LargeForms` is
committed). Each size is 25-field sections cycling through ten field kinds with their rules, plus up to 1,000
line items with a reaction-maintained total, a nested party and a `SUBMIT` operation.

```bash
# In-process stages, at every size (BenchmarkDotNet)
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*LargeForm*"

# End to end over HTTP against the running sample: p50 / p95 per endpoint, plus server stages
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- load http://localhost:5101 5 load-report.md
```

`deep` (`DeepForms/`) is written by hand, since one recursive definition serves any depth; `DeepFormFiller`
grows a tree of a given shape. The benchmarks use about 1,000 field values at depth 1, 2, 3 and 4 with the
same number of components, so only the depth changes:

```bash
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*DeepForm*"
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- load http://localhost:5101 5 load-deep.md deep

# A long history: the depth-4 tree filled 3 edits per commit up to 1,000 commits, then drafts that are
# 1 / 10 / 100 / 1,000 commits behind rebased; and the rebase in process
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- history http://localhost:5101 1000 3 history.md
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*DeepHistory*"

# Memory: what a loaded form retains, and how big it is when stored, for every form
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- memory memory.md

# Capacity: completely filled 1,000 / 2,000-field forms, flat and four levels deep
dotnet run -c Release --project x86cc.KVBind.Benchmarks -- capacity capacity.md
```

The results and the hot spots found are in [PERFORMANCE.md](PERFORMANCE.md).
