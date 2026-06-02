using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace x86cc.KVBind.Core;

internal sealed class KVAllowedValuesDefinition
{
    private readonly Func<object?, bool> _isAllowed;
    private readonly Func<object?, object?> _normalizeForStorage;
    private readonly Func<object?, object?> _denormalizeFromStorage;
    private readonly IReadOnlyDictionary<string, KVExplicitAllowedValue>? _entriesById;
    private readonly IReadOnlyDictionary<object, KVExplicitAllowedValue>? _entriesByValue;

    private KVAllowedValuesDefinition(
        Func<object?, bool> isAllowed,
        Func<object?, object?> normalizeForStorage,
        Func<object?, object?> denormalizeFromStorage,
        IReadOnlyDictionary<string, KVExplicitAllowedValue>? entriesById = null,
        IReadOnlyDictionary<object, KVExplicitAllowedValue>? entriesByValue = null)
    {
        _isAllowed = isAllowed;
        _normalizeForStorage = normalizeForStorage;
        _denormalizeFromStorage = denormalizeFromStorage;
        _entriesById = entriesById;
        _entriesByValue = entriesByValue;
    }

    public static KVAllowedValuesDefinition Create<TValue>(IEnumerable<TValue> values)
    {
        var allowedSet = new HashSet<TValue>(values);
        return new KVAllowedValuesDefinition(
            isAllowed: value => value is TValue typed && allowedSet.Contains(typed),
            normalizeForStorage: value => value,
            denormalizeFromStorage: value => value);
    }

    public static KVAllowedValuesDefinition CreateExplicit(IReadOnlyCollection<KVExplicitAllowedValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var entriesById = new Dictionary<string, KVExplicitAllowedValue>(StringComparer.Ordinal);
        var entriesByValue = new Dictionary<object, KVExplicitAllowedValue>();
        foreach (var entry in values)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                continue;
            }

            var placeholders = entry.Placeholders?
                .GroupBy(static p => p.Name, StringComparer.Ordinal)
                .Select(static g => g.First())
                .ToArray();
            var normalizedEntry = entry with { Placeholders = placeholders };
            entriesById[entry.Id] = normalizedEntry;
            if (entry.Value is not null)
            {
                entriesByValue[entry.Value] = normalizedEntry;
            }
        }

        return new KVAllowedValuesDefinition(
            isAllowed: value => value is string id
                ? entriesById.ContainsKey(id)
                : value is not null && entriesByValue.ContainsKey(value),
            normalizeForStorage: value =>
            {
                if (value is null)
                {
                    return null;
                }

                if (value is string id)
                {
                    if (entriesById.TryGetValue(id, out _))
                    {
                        return id;
                    }

                    throw new InvalidOperationException("Stored value token is not part of allowed values.");
                }

                if (entriesByValue.TryGetValue(value, out var entry))
                {
                    return entry.Id;
                }

                throw new InvalidOperationException("Value type mismatch for allowed values.");
            },
            denormalizeFromStorage: value =>
            {
                if (value is null)
                {
                    return null;
                }

                if (value is string id && entriesById.TryGetValue(id, out var entry))
                {
                    return entry.Value;
                }

                throw new InvalidOperationException("Stored value token is not part of allowed values.");
            },
            entriesById,
            entriesByValue);
    }

    public static KVAllowedValuesDefinition Create<TValue, TToken>(
        IEnumerable<TValue> values,
        Func<TValue, TToken> tokenSelector,
        Func<TValue, string>? labelSelector = null)
        where TToken : notnull
    {
        var valueToToken = new Dictionary<object, TToken>();
        var tokenToValue = new Dictionary<TToken, TValue>();
        foreach (var value in values)
        {
            var token = tokenSelector(value);
            valueToToken[value!] = token;
            tokenToValue[token] = value;
        }

        return new KVAllowedValuesDefinition(
            isAllowed: value => value is TValue typed && valueToToken.ContainsKey(typed!),
            normalizeForStorage: value => value is TValue typed
                ? valueToToken[typed!]
                : throw new InvalidOperationException("Value type mismatch for allowed values."),
            denormalizeFromStorage: value => value is TToken token && tokenToValue.TryGetValue(token, out var typed)
                ? typed
                : throw new InvalidOperationException("Stored value token is not part of allowed values."));
    }

    public bool IsAllowed(object? value)
    {
        return _isAllowed(value);
    }

    public object? DenormalizeFromStorage(object? value)
    {
        return _denormalizeFromStorage(value);
    }
}

internal readonly record struct KVAllowedValuePlaceholder(string Name, string Label, Type ValueType, bool IsRequired);

internal readonly record struct KVExplicitAllowedValue(
    string Id,
    string Label,
    object Value,
    string? Template = null,
    IReadOnlyList<KVAllowedValuePlaceholder>? Placeholders = null);
