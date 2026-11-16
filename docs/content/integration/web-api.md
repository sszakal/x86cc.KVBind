---
title: Web APIs
sidebar_position: 3
---

# Web APIs

Patches are designed to travel over HTTP: a client sends a list of operations, the server applies them to the user's draft and answers with the change set and validation. This page shows how to wire that up in ASP.NET Core; the [sample application](/samples) has a complete generic API.

## Accepting patch operations

A request DTO can carry operations as `{ operationCode, path, value }`, with the value left as raw JSON:

```csharp
public sealed record PatchOperationRequest(string OperationCode, string Path, JsonElement? Value = null);
```

Map each request to a `KVPatchOperation`. `SET` values and custom-operation arguments can be passed as the `JsonElement` itself, because KVBind [converts it to the field's or parameter's type](/patching/operations#value-types). The structural operations need their payload types:

```csharp
static KVPatchOperation ToOperation(PatchOperationRequest request)
{
    var code = request.OperationCode.ToUpperInvariant();
    var value = request.Value is { ValueKind: not JsonValueKind.Null } json ? json : (JsonElement?)null;

    return code switch
    {
        KVPatchOperations.Add  => new(code, request.Path, value?.Deserialize<KVAddPatchPayload>(JsonSerializerOptions.Web)),
        KVPatchOperations.Move => new(code, request.Path, value?.Deserialize<KVMovePatchPayload>(JsonSerializerOptions.Web)),
        KVPatchOperations.Init => new(code, request.Path, value?.GetString()),
        _                      => new(code, request.Path, value),   // SET, UNSET, REMOVE, DROP, DISCARD, RESET, custom
    };
}
```

| Operation | JSON value |
| --- | --- |
| `SET` | the new value (`"text"`, `12.5`, `true`, `["a","b"]`, `null` to clear) |
| `ADD` | `{ "itemId": "shipping", "typeToken": "Line" }` (`typeToken` only for polymorphic collections) |
| `MOVE` | `{ "toIndex": 0 }` |
| `INIT` | the subtype token, e.g. `"COMPANY"` |
| custom | the method's argument, if it has one |
| others | none |

## Handling errors

A patch that cannot be applied throws, without a dedicated exception type:

- `InvalidOperationException` — unknown path, wrong target kind, a value that does not convert, a disallowed or read-only operation, an unknown allowed-value token on a non-text field;
- `ArgumentException` (including `ArgumentOutOfRangeException`) — malformed arguments such as an invalid item id or a move index out of range.

These are client errors, so translate them to `400 Bad Request` at the patch call — and only there, so that genuine defects elsewhere still surface as `500`.

**A patch is not transactional.** Operations run in order, and when one throws, the operations before it stay applied to the in-memory draft. Do not save the draft after a failed patch; load it again for the next request.

```csharp
KVPatchResult result;
try
{
    result = agreement.Patch(request.Operations.Select(ToOperation));
}
catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
{
    return Results.BadRequest(new { error = ex.Message });   // the draft is not saved
}

save(overlay);
return Results.Ok(new
{
    changes = result.Changes.Select(c => new { c.Path, type = c.ChangeType.ToString(), c.OldValue, c.NewValue }),
    errors = result.Validate().Errors.Select(e => new { e.Path, e.Code, e.Message }),
});
```

## Status codes

| Situation | Suggested response |
| --- | --- |
| Patch operation rejected | `400 Bad Request` with the message |
| Commit of a draft whose base is not the latest commit | `409 Conflict` — the client should [rebase](/workflows/drafts-and-rebase) |
| Concurrent write lost (optimistic concurrency) | `409 Conflict` |
| `CreateCommit` throws `KVChangeSetValidationException` | `422 Unprocessable Entity` with `exception.Errors` |

## Describing the model to clients

Serve the [schema](/schema) from an endpoint so a client can render forms, labels, allowed values and available operations without duplicating the model:

```csharp
var schema = KVSchemaMapper.Map<Agreement>(registry);   // build once
app.MapGet("/api/agreements/definition", () => schema);
```
