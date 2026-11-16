---
title: Overview
sidebar_position: 1
slug: /
---

# KVBind

**KVBind** is a Git-inspired runtime for versioned business object graphs.

It lets you build large, document-like aggregates as ordinary strongly typed C# classes while storing their state in a canonical, versioned model. From that single representation KVBind provides draft editing, replayable commits, diffing, patch APIs, validation, schema evolution and runtime change notifications — so each application does not have to implement them again.

<video controls muted playsInline preload="metadata" poster="/video/kvbind-demo.jpg" src="/video/kvbind-demo.mp4" style={{width: '100%', borderRadius: 12}}></video>

<small>A 95-second tour of the [sample application](/samples): one generic UI driven by definitions, drafts, validation, collections, change reactions, commits, and a rebase with a conflict.</small>

## Where it fits

KVBind suits systems where business documents evolve over time and every change matters:

- contract management, rights and licensing
- land and government registers
- insurance policies, claims and underwriting
- medical records and regulatory filings
- engineering documents and product lifecycle management

Typical characteristics of these domains:

- deeply nested object graphs and long editing sessions
- draft workflows, concurrent editing and partial updates
- audit requirements
- schemas that keep evolving

## What you get

| Capability | Where to read |
| --- | --- |
| Typed C# models backed by canonical path/value storage | [Mental model](/concepts/mental-model), [Paths](/concepts/paths), [Source generation](/concepts/source-generation) |
| Schema declared once with a fluent DSL | [Definition DSL](/dsl/fields-and-groups) |
| Drafts, commits, diffs, rebase with conflict review | [Drafts and rebase](/workflows/drafts-and-rebase) |
| A path-based patch API for clients, plus domain operations | [Patch operations](/patching/operations), [Custom operations](/patching/custom-operations) |
| Profile-based validation, value rules and collection rules | [Validation](/behavior/validation) |
| Change reactions and domain events | [Change reactions](/behavior/change-reactions), [Domain events](/behavior/domain-events) |
| Schema migrations as replayable commits | [Schema migrations](/workflows/migrations) |
| Storage-agnostic persistence and HTTP-friendly patches | [Persistence](/integration/persistence), [Web APIs](/integration/web-api) |
| A serializer-neutral schema for generic UIs | [Schema package](/schema), [Reading definitions](/advanced/definitions) |

## Packages

| Package | Purpose |
| --- | --- |
| `x86cc.KVBind.Core` | The runtime: snapshots, overlays, typed nodes, the DSL, patching, validation, reactions, migrations. |
| `x86cc.KVBind.SourceGenerator` | A Roslyn incremental generator that implements `[KVBind]` partial properties. |
| `x86cc.KVBind.Schema` | Maps a definition to a plain, serializable schema document. |

## Status

KVBind is actively evolving toward a standalone package. APIs may change while the runtime is hardened; see the [changelog](https://github.com/sszakal/x86cc.KVBind/blob/main/CHANGELOG.md) for behavior changes between versions.
