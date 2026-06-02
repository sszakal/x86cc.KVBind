using System;
using System.Collections.Generic;
using System.Linq;

namespace x86cc.KVBind.Core;

public enum KVLayoutDiagnosticSeverity
{
    Warning,
    Error
}

public sealed record KVLayoutDiagnostic(
    string Code,
    string Path,
    string Message,
    KVLayoutDiagnosticSeverity Severity);

public sealed class KVLayoutValidationResult
{
    public KVLayoutValidationResult(IReadOnlyList<KVLayoutDiagnostic> diagnostics)
    {
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public IReadOnlyList<KVLayoutDiagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(static diagnostic => diagnostic.Severity != KVLayoutDiagnosticSeverity.Error);

    public IReadOnlyList<KVLayoutDiagnostic> Errors => Diagnostics
        .Where(static diagnostic => diagnostic.Severity == KVLayoutDiagnosticSeverity.Error)
        .ToArray();

    public IReadOnlyList<KVLayoutDiagnostic> Warnings => Diagnostics
        .Where(static diagnostic => diagnostic.Severity == KVLayoutDiagnosticSeverity.Warning)
        .ToArray();
    
}
