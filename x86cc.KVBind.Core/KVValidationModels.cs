using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVValidationResult(IReadOnlyList<KVValidationError> errors, IReadOnlyList<string> scope, bool isFullEvaluation)
{
    public IReadOnlyList<KVValidationError> Errors { get; } = errors;

    public IReadOnlyList<string> Scope { get; } = scope;

    public bool IsFullEvaluation { get; } = isFullEvaluation;
}

public sealed class KVValidationError(string path, string code, string? message = null)
{
    public string Path { get; } = path;

    public string Code { get; } = code;

    public string? Message { get; } = message;
}

public sealed class KVChangeSetValidationException(IReadOnlyList<KVValidationError> errors) : Exception("Change set validation failed.")
{
    public IReadOnlyList<KVValidationError> Errors { get; } = errors;
}
