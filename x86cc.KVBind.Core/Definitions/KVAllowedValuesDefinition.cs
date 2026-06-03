using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

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
            isAllowed: value => value is TValue typed && valueToToken.ContainsKey(typed!)
                              || value is TToken token && tokenToValue.ContainsKey(token),
            normalizeForStorage: value => value is TValue typed
                ? valueToToken[typed!]
                : throw new InvalidOperationException("Value type mismatch for allowed values."),
            denormalizeFromStorage: value => value is TToken token && tokenToValue.TryGetValue(token, out var typed)
                ? typed
                : throw new InvalidOperationException("Stored value token is not part of allowed values."));
    }

    public static KVAllowedValuesDefinition CreateForElements<TElement, TToken>(
        IEnumerable<TElement> values,
        Func<TElement, TToken> tokenSelector,
        Func<TElement, string>? labelSelector = null)
        where TToken : notnull
    {
        var valueToToken = new Dictionary<object, TToken>();
        var tokenToValue = new Dictionary<TToken, TElement>();
        foreach (var value in values)
        {
            var token = tokenSelector(value);
            valueToToken[value!] = token;
            tokenToValue[token] = value;
        }

        return new KVAllowedValuesDefinition(
            isAllowed: value => TryReadSequence(value, out var items) && items.All(IsAllowedItem),
            normalizeForStorage: value => NormalizeSequence<TToken>(value, IsAllowedItem, NormalizeItem),
            denormalizeFromStorage: value => DenormalizeSequence(value, tokenToValue));

        bool IsAllowedItem(object? value)
        {
            return value is TElement typed && valueToToken.ContainsKey(typed!)
                   || TryConvertToken<TToken>(value, out var token) && tokenToValue.ContainsKey(token);
        }

        TToken NormalizeItem(object? value)
        {
            if (value is TElement typed)
            {
                return valueToToken[typed!];
            }

            if (TryConvertToken<TToken>(value, out var token) && tokenToValue.ContainsKey(token))
            {
                return token;
            }

            throw new InvalidOperationException("Value type mismatch for allowed values.");
        }
    }

    public static KVAllowedValuesDefinition CreateExplicitForElements<TElement>(IReadOnlyCollection<KVExplicitAllowedValue> values)
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
            isAllowed: value => TryReadSequence(value, out var items) && items.All(IsAllowedItem),
            normalizeForStorage: value => NormalizeSequence<string>(value, IsAllowedItem, NormalizeItem),
            denormalizeFromStorage: value => DenormalizeSequence(value, entriesById.ToDictionary(pair => pair.Key, pair => (TElement)pair.Value.Value, StringComparer.Ordinal)));

        bool IsAllowedItem(object? value)
        {
            return value is string id
                ? entriesById.ContainsKey(id)
                : value is not null && entriesByValue.ContainsKey(value);
        }

        string NormalizeItem(object? value)
        {
            if (value is string id)
            {
                if (entriesById.TryGetValue(id, out _))
                {
                    return id;
                }

                throw new InvalidOperationException("Stored value token is not part of allowed values.");
            }

            if (value is not null && entriesByValue.TryGetValue(value, out var entry))
            {
                return entry.Id;
            }

            throw new InvalidOperationException("Value type mismatch for allowed values.");
        }
    }

    public bool IsAllowed(object? value)
    {
        return _isAllowed(value);
    }

    public object? DenormalizeFromStorage(object? value)
    {
        return _denormalizeFromStorage(value);
    }

    public object? NormalizeForStorage(object? value)
    {
        return _normalizeForStorage(value);
    }

    public object? DenormalizeFromStorage(object? value, Type targetType)
    {
        var denormalized = _denormalizeFromStorage(value);
        if (denormalized is not IReadOnlyList<object?> items)
        {
            return denormalized;
        }

        var elementType = GetCollectionElementType(targetType);
        if (elementType is null)
        {
            return denormalized;
        }

        if (targetType.IsArray)
        {
            var array = Array.CreateInstance(elementType, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                array.SetValue(items[i], i);
            }

            return array;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in items)
        {
            list.Add(item);
        }

        return list;
    }

    private static List<TResult> NormalizeSequence<TResult>(object? value, Func<object?, bool> isAllowed, Func<object?, TResult> normalizeItem)
    {
        if (!TryReadSequence(value, out var items))
        {
            throw new InvalidOperationException("Value type mismatch for allowed values.");
        }

        var result = new List<TResult>();
        foreach (var item in items)
        {
            if (!isAllowed(item))
            {
                throw new InvalidOperationException("Stored value token is not part of allowed values.");
            }

            result.Add(normalizeItem(item));
        }

        return result;
    }

    private static object? DenormalizeSequence<TToken, TElement>(object? value, IReadOnlyDictionary<TToken, TElement> tokenToValue)
        where TToken : notnull
    {
        if (!TryReadSequence(value, out var items))
        {
            throw new InvalidOperationException("Stored value token is not part of allowed values.");
        }

        var denormalized = new List<object?>();
        foreach (var item in items)
        {
            if (TryConvertToken(item, out TToken token) && tokenToValue.TryGetValue(token, out var typed))
            {
                denormalized.Add(typed);
                continue;
            }

            if (item is TElement element)
            {
                denormalized.Add(element);
                continue;
            }

            throw new InvalidOperationException("Stored value token is not part of allowed values.");
        }

        return denormalized;
    }

    private static bool TryReadSequence(object? value, out IReadOnlyList<object?> items)
    {
        if (value is null or string)
        {
            if (value is string text && TryParseJsonArray(text, out items))
            {
                return true;
            }

            items = [];
            return false;
        }

        if (value is JsonElement json)
        {
            return TryReadJsonArray(json, out items);
        }

        if (value is IEnumerable enumerable)
        {
            var list = new List<object?>();
            foreach (var item in enumerable)
            {
                list.Add(item);
            }

            items = list;
            return true;
        }

        items = [];
        return false;
    }

    private static bool TryParseJsonArray(string text, out IReadOnlyList<object?> items)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return TryReadJsonArray(document.RootElement, out items);
        }
        catch (JsonException)
        {
            items = [];
            return false;
        }
    }

    private static bool TryReadJsonArray(JsonElement json, out IReadOnlyList<object?> items)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            items = [];
            return false;
        }

        var list = new List<object?>();
        foreach (var element in json.EnumerateArray())
        {
            list.Add(element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.Clone()
            });
        }

        items = list;
        return true;
    }

    private static bool TryConvertToken<TToken>(object? value, out TToken token)
        where TToken : notnull
    {
        if (value is TToken typed)
        {
            token = typed;
            return true;
        }

        if (value is not null)
        {
            try
            {
                token = (TToken)Convert.ChangeType(value, typeof(TToken), CultureInfo.InvariantCulture);
                return true;
            }
            catch (InvalidCastException)
            {
            }
            catch (FormatException)
            {
            }
            catch (OverflowException)
            {
            }
        }

        token = default!;
        return false;
    }

    private static Type? GetCollectionElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return type.GetGenericArguments()[0];
        }

        return null;
    }
}

internal readonly record struct KVAllowedValuePlaceholder(string Name, string Label, Type ValueType, bool IsRequired);

internal readonly record struct KVExplicitAllowedValue(
    string Id,
    string Label,
    object Value,
    string? Template = null,
    IReadOnlyList<KVAllowedValuePlaceholder>? Placeholders = null);
