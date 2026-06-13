using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public class KVDefinition
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyAnnotations =
        new Dictionary<string, object?>();

    private Dictionary<string, object?>? _annotations;

    public required string SubSegmentPath { get; init; }

    /// <summary>
    /// Human-friendly label for this field / group / collection / nested node, declared in the DSL via
    /// <c>DisplayName(...)</c> (or a <c>[KVBind(DisplayName = "...")]</c> attribute on the model property).
    /// Consumers fall back to <see cref="SubSegmentPath"/> when this is null.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Opaque consumer metadata attached in the DSL via <c>Annotate(key, value)</c> — e.g. UI hints
    /// (<c>"ui:control" = "multiselect"</c>). KVBind stores and carries these through but never interprets
    /// them; the keys and meaning are entirely the consumer's. Empty unless annotated.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Annotations => _annotations ?? EmptyAnnotations;

    internal void Annotate(string key, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        (_annotations ??= new Dictionary<string, object?>(StringComparer.Ordinal))[key] = value;
    }

    internal void AddAnnotations(IReadOnlyDictionary<string, object?> source)
    {
        if (source.Count == 0) return;
        _annotations ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in source) _annotations[key] = value;
    }
}