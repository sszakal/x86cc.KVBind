using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public class KVFieldDefinition : KVDefinition
{
    public bool IsRequired { get; init; }

    // The declared CLR type of the field (the model property's type). A SET patch converts its value to
    // this type or is rejected; typeof(object) means "unknown", which skips the check.
    public Type ValueType { get; init; } = typeof(object);

    // Root-only marker: when the aggregate is bound with a parent snapshot, this member is read-only and
    // sourced from the parent (see KVOverlay inheritance). Editable on a parentless master.
    public bool IsInherited { get; init; }

    public List<KVCompiledValidationRule> ValidationRules { get; } = new();

    // Create-time default, materialized into the overlay by ApplyDefaults where the field is unset.
    // HasDefault distinguishes "default is null/false/0" from "no default declared".
    public bool HasDefault { get; init; }

    public object? DefaultValue { get; init; }

    // Value rules declared on the field itself (they apply under every validation profile). Unset values
    // are never reported by these; combine with IsRequired.
    public int? MinLength => this.Constraints?.MinLength;

    public int? MaxLength => this.Constraints?.MaxLength;

    // A regular expression the whole text must match.
    public string? Pattern => this.Constraints?.Pattern;

    public object? Min => this.Constraints?.Min;

    public object? Max => this.Constraints?.Max;

    internal KVAllowedValuesDefinition? AllowedValues { get; init; }

    internal KVValueConstraints? Constraints { get; init; }
}
