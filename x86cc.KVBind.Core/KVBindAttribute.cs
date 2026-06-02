using System;

namespace x86cc.KVBind.Core;

[AttributeUsage(AttributeTargets.Property)]
public sealed class KVBindAttribute(string canonicalKey) : Attribute
{
    public string CanonicalKey { get; } = canonicalKey;
}
