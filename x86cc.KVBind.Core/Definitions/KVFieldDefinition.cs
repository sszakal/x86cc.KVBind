using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public class KVFieldDefinition: KVDefinition
{
    public bool IsRequired { get; init; }

    // Root-only marker: when the aggregate is bound with a parent snapshot, this member is read-only and
    // sourced from the parent (see KVOverlay inheritance). Editable on a parentless master.
    public bool IsInherited { get; init; }

    internal KVAllowedValuesDefinition? AllowedValues { get; init; }

    public List<KVCompiledValidationRule> ValidationRules { get; } = new();

    // Create-time default, materialized into the overlay by ApplyDefaults where the field is unset.
    // HasDefault distinguishes "default is null/false/0" from "no default declared".
    public bool HasDefault { get; init; }
    public object? DefaultValue { get; init; }
}
