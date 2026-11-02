using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVFieldGroupOptionsBuilder
{
    private readonly Dictionary<string, object?> annotations = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, object?> Annotations => this.annotations;

    internal bool? IsResettable { get; private set; }

    internal bool IsInherited { get; private set; }

    internal string? DisplayNameValue { get; private set; }

    public KVFieldGroupOptionsBuilder DisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        this.DisplayNameValue = displayName;
        return this;
    }

    public KVFieldGroupOptionsBuilder Annotate(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.annotations[key] = value;
        return this;
    }

    public KVFieldGroupOptionsBuilder Resettable(bool isResettable = true)
    {
        this.IsResettable = isResettable;
        return this;
    }

    // Root-only: the whole group is inherited — read-only and parent-sourced when bound with a parent.
    public KVFieldGroupOptionsBuilder Inherited()
    {
        this.IsInherited = true;
        return this;
    }
}