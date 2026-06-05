using System;

namespace x86cc.KVBind.Core;

[AttributeUsage(AttributeTargets.Property)]
public sealed class KVBindAttribute(string canonicalKey) : Attribute
{
    public string CanonicalKey { get; } = canonicalKey;

    /// <summary>
    /// Optional human-friendly label for this property. Used by the definition as a fallback display name
    /// when the DSL does not declare one via <c>DisplayName(...)</c>.
    /// </summary>
    public string? DisplayName { get; set; }
}
