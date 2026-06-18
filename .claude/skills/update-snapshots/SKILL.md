---
name: update-snapshots
description: How to handle Meziantou InlineSnapshotTesting failures in KVBind unit tests — understanding, reviewing, and accepting inline snapshot changes. Use when a snapshot assertion fails or expected output must be regenerated.
---

# Inline snapshot tests

Some unit tests use `Meziantou.Framework.InlineSnapshotTesting`. The expected value is embedded directly in the test source; serialization is configured once in `x86cc.KVBind.UnitTests/AssemblyInitializer.cs`.

## When a snapshot test fails

A snapshot failure means the serialized actual output no longer matches the inline expected literal. Two possibilities:

1. **A real regression** — the change in behavior is unintended. Fix the code, do not touch the snapshot.
2. **An intended change** — the new output is correct and the inline literal should be updated.

Decide which before accepting anything. Read the diff in the failure message; treat an unexpected diff as a bug, not a snapshot to rubber-stamp.

## Accepting intended changes

InlineSnapshotTesting can rewrite the expected literal in the test source for you. The update behavior is controlled by its configuration / environment (the launcher or `InlineSnapshotSettings`), not by editing the string blindly. Prefer letting the framework rewrite the literal so formatting and escaping stay canonical, then review the resulting source diff.

If editing by hand is unavoidable, match the exact serializer formatting configured in `AssemblyInitializer.cs` — whitespace and ordering are significant.

## After updating

- Re-run the affected tests (use the `run-tests` skill with a `--filter`) and confirm green.
- Review the snapshot diff as part of your change — a large or surprising snapshot delta is a signal to re-examine the behavior, not to accept faster.
- Do not commit generated analyzer artifacts under `obj/.../generated`.
