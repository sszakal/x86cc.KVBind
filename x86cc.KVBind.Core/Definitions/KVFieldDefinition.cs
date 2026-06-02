using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public class KVFieldDefinition: KVDefinition
{
    public bool IsRequired { get; init; }

    internal KVAllowedValuesDefinition? AllowedValues { get; init; }

    public List<KVCompiledValidationRule> ValidationRules { get; } = new();
}
