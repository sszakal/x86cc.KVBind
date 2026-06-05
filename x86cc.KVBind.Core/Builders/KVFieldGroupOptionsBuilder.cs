using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVFieldGroupOptionsBuilder
{
    private readonly HashSet<string> _tags = new(StringComparer.Ordinal);

    internal IReadOnlyCollection<string> Tags => _tags;

    internal bool? IsResettable { get; private set; }

    internal string? DisplayNameValue { get; private set; }

    public KVFieldGroupOptionsBuilder DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayNameValue = displayName;
        return this;
    }

    public KVFieldGroupOptionsBuilder Tag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        _tags.Add(tag);
        return this;
    }

    public KVFieldGroupOptionsBuilder Resettable(bool isResettable = true)
    {
        IsResettable = isResettable;
        return this;
    }
}