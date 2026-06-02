using System;
using System.Collections.Generic;
using System.Linq;

namespace x86cc.KVBind.Core;

public sealed class KVAllowedValueComponentBuilder
{
    private readonly List<KVAllowedValuePlaceholder> _placeholders = [];

    internal string? TemplateValue { get; private set; }

    public KVAllowedValueComponentBuilder Template(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new ArgumentException("Template is required.", nameof(template));
        }

        TemplateValue = template;
        return this;
    }

    public KVAllowedValueComponentBuilder Placeholder<TValue>(string name, bool required = true)
    {
        return Placeholder<TValue>(name, name, required);
    }

    public KVAllowedValueComponentBuilder Placeholder<TValue>(string name, string label, bool required = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Placeholder name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Placeholder label is required.", nameof(label));
        }

        if (_placeholders.Any(existing => string.Equals(existing.Name, name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Placeholder '{name}' is already defined.");
        }

        _placeholders.Add(new KVAllowedValuePlaceholder(name, label, typeof(TValue), required));
        return this;
    }

    internal IReadOnlyList<KVAllowedValuePlaceholder>? BuildPlaceholders()
    {
        return _placeholders.Count == 0 ? null : _placeholders;
    }
}
