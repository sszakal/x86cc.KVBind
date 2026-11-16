---
title: Sample application
sidebar_position: 9
---

# Sample application

The repository's [`samples/`](https://github.com/sszakal/x86cc.KVBind/tree/main/samples) folder holds an Aspire app — an API, Postgres via Marten and an Angular UI — that serves several KVBind models through **one generic API and one generic UI**. Nothing in either is written per model: each model's form, labels, options, rules and allowed operations come from its KVBind definition, via the [schema package](/schema).

```bash
ASPIRE_ALLOW_UNSECURED_TRANSPORT=true dotnet run --project samples/x86cc.KVBind.Sample.AppHost --launch-profile http
```

| Resource | URL |
| --- | --- |
| UI | `http://localhost:5200` |
| API (Swagger) | `http://localhost:5101/swagger` |
| Aspire dashboard (traces show each request's KVBind stages) | `http://localhost:15016` |

## Models

| Route name | What it shows |
| --- | --- |
| `claims` | The simple example: an insurance claim with a policy group, damaged items, notes and a polymorphic claimant. |
| `underwriting` | Every KVBind feature in one form. |
| `large-100` … `large-2000` | Generated forms of 100 / 500 / 1000 / 2000 fields for the performance demo. |
| `deep` | A recursive tree of components whose `Detail` slot can hold an `ASSEMBLY` of components again. Costs roll up through every level. |

## Generic API

All routes live under `/api/models/{model}/...`:

| Method and path | Purpose |
| --- | --- |
| `GET /api/models` | the catalog |
| `GET {model}/definition` | the schema generated from the definition |
| `POST {model}` · `GET {model}` | create (optionally with a `parentId`) · list |
| `GET {model}/{id}/snapshot` · `/changesets` | committed data · history with changes and domain events |
| `POST {model}/{id}/drafts` · `GET …/drafts/{draftId}` | open or resume a user's draft · read it |
| `POST …/patch` | patch operations, built-in or custom (`SUBMIT`, `APPROVE`, …) |
| `POST …/validate` · `/reset` · `/commit` | validate · discard all · commit (`409` stale, `422` invalid) |
| `POST …/rebase` · `/rebase/resolve` · `/rebase/finish` · `DELETE …/rebase` | [rebase](/workflows/drafts-and-rebase) onto the latest commit |
| `POST {model}/legacy` · `POST {model}/migrate` | store an old-layout aggregate · [migrate](/workflows/migrations) every stored aggregate in one batch |

Every response carries a `Server-Timing` header with the KVBind stages of the request (load, migrate, bind, patch, react, validate, commit, project, persist). The same stages are OpenTelemetry spans in the Aspire dashboard.

## Underwriting: one section per feature

The scenario tests in `x86cc.KVBind.IntegrationTests/Underwriting` follow the same map.

| Feature | Where in the model |
| --- | --- |
| Scalar types and defaults | `Basics`: string, numbers, bool, dates and times, `Guid`, enums and `[Flags]` enums |
| Allowed values | `Classification`: labelled, plain, numeric, components with placeholders, element-wise |
| Value rules | `Contact`: `Required`, length, `Pattern`, `Min`/`Max`/`Range`, profile-only rules |
| Profiles and group rules | submit profile once `Status` leaves draft; conditional `When` rules |
| Field groups | `Premises`: resettable, defaults re-applied, nested `Protection` group |
| Collections | `Locations` (BUILDING / VEHICLE), seeded `Questions`, count and aggregate rules |
| `AllowOperations` / `PatchReadOnly` | locked `Questions`, `Contacts` without `MOVE`, read-only `Status`, `Rating`, `Endorsements` |
| Custom operations | `SUBMIT`, `QUOTE`, `BIND`, `DECLINE` on the root, `APPROVE` on a location, `ADD_ENDORSEMENT` on a collection |
| Nested nodes | `Insured` (PERSON / COMPANY, default COMPANY, required), `Vessel` |
| Change reactions | totals follow Value / Added / Removed / Moved / Replaced changes |
| Domain events | raised by operations, pending on the draft, published with the commit |
| Drafts, rebase, concurrency | every conflict kind, Ours / Theirs / Custom, stale commits |
| Migrations | rename + backfill, replace nested type, remove + backfill; on read and in batch |
| Inheritance | `Program` terms inherited from a parent application |

## Performance

Benchmarks (BenchmarkDotNet stages plus HTTP load, history, memory and capacity runs) live in `x86cc.KVBind.Benchmarks`. The results and the hot spots found are in [`samples/PERFORMANCE.md`](https://github.com/sszakal/x86cc.KVBind/blob/main/samples/PERFORMANCE.md).
