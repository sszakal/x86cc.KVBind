using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace x86cc.KVBind.Core;

internal readonly record struct KVAllowedValuePlaceholder(string Name, string Label, Type ValueType, bool IsRequired);

internal readonly record struct KVExplicitAllowedValue(
    string Id,
    string Label,
    object Value,
    string? Template = null,
    IReadOnlyList<KVAllowedValuePlaceholder>? Placeholders = null);

internal sealed class KVAllowedValuesDefinition
{
    private readonly Func<object?, bool> isAllowed;
    private readonly Func<object?, object?> normalizeForStorage;
    private readonly Func<object?, object?> denormalizeFromStorage;
    private readonly IReadOnlyDictionary<string, KVExplicitAllowedValue>? entriesById;
    private readonly IReadOnlyDictionary<object, KVExplicitAllowedValue>? entriesByValue;

    private KVAllowedValuesDefinition(
        Func<object?, bool> isAllowed,
        Func<object?, object?> normalizeForStorage,
        Func<object?, object?> denormalizeFromStorage,
        IReadOnlyDictionary<string, KVExplicitAllowedValue>? entriesById = null,
        IReadOnlyDictionary<object, KVExplicitAllowedValue>? entriesByValue = null)
    {
        this.isAllowed = isAllowed;
        this.normalizeForStorage = normalizeForStorage;
        this.denormalizeFromStorage = denormalizeFromStorage;
        this.entriesById = entriesById;
        this.entriesByValue = entriesByValue;
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

        var index = ExplicitValueIndex.Build(values);
        return new KVAllowedValuesDefinition(
            isAllowed: index.IsAllowed,
            normalizeForStorage: value => value is null ? null : index.NormalizeItem(value),
            denormalizeFromStorage: index.Denormalize,
            index.ById,
            index.ByValue);
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
            isAllowed: value => (value is TValue typed && valueToToken.ContainsKey(typed!))
                              || (value is TToken token && tokenToValue.ContainsKey(token)),
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
            return (value is TElement typed && valueToToken.ContainsKey(typed!))
                   || (TryConvertToken<TToken>(value, out var token) && tokenToValue.ContainsKey(token));
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

        var index = ExplicitValueIndex.Build(values);

        // Built once. An entry whose value is not a TElement (e.g. registered with a null value) has no
        // element to read back, so it is left out and only fails when a stored sequence references its id.
        var elementsById = new Dictionary<string, TElement>(StringComparer.Ordinal);
        foreach (var (id, entry) in index.ById)
        {
            if (entry.Value is TElement element)
            {
                elementsById[id] = element;
            }
        }

        return new KVAllowedValuesDefinition(
            isAllowed: value => TryReadSequence(value, out var items) && items.All(index.IsAllowed),
            normalizeForStorage: value => NormalizeSequence<string>(value, index.IsAllowed, index.NormalizeItem),
            denormalizeFromStorage: value => DenormalizeSequence(value, elementsById));
    }

    public bool IsAllowed(object? value)
    {
        return this.isAllowed(value);
    }

    public object? DenormalizeFromStorage(object? value)
    {
        return this.denormalizeFromStorage(value);
    }

    public object? NormalizeForStorage(object? value)
    {
        return this.normalizeForStorage(value);
    }

    public object? DenormalizeFromStorage(object? value, Type targetType)
    {
        var denormalized = this.denormalizeFromStorage(value);
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
        if (value is string text)
        {
            return TryParseJsonArray(text, out items);
        }

        if (value is JsonElement json)
        {
            return TryReadJsonArray(json, out items);
        }

        if (value is IEnumerable enumerable)
        {
            items = enumerable.Cast<object?>().ToList();
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

    // long when integral, decimal when it fits, otherwise double (1e30 is a valid JSON number).
    private static object ReadJsonNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        return element.TryGetDecimal(out var decimalValue) ? decimalValue : element.GetDouble();
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
                JsonValueKind.Number => ReadJsonNumber(element),
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

    // Explicit allowed values indexed by id and by value; an entry without an id is ignored, and a later
    // entry with the same id or value replaces the earlier one.
    private sealed class ExplicitValueIndex
    {
        private static readonly HashSet<Type> NumericTypes =
        [
            typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
        ];

        public Dictionary<string, KVExplicitAllowedValue> ById { get; } = new(StringComparer.Ordinal);

        public Dictionary<object, KVExplicitAllowedValue> ByValue { get; } = [];

        // The runtime types of the registered values, for matching a number that arrives as another
        // numeric type (JSON yields long / decimal for an int or enum value).
        private HashSet<Type> ValueTypes { get; } = [];

        public static ExplicitValueIndex Build(IReadOnlyCollection<KVExplicitAllowedValue> values)
        {
            var index = new ExplicitValueIndex();
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
                index.ById[entry.Id] = normalizedEntry;
                if (entry.Value is not null)
                {
                    index.ByValue[entry.Value] = normalizedEntry;
                    index.ValueTypes.Add(entry.Value.GetType());
                }
            }

            return index;
        }

        public bool IsAllowed(object? value)
        {
            return value is string id
                ? this.ById.ContainsKey(id)
                : value is not null && this.TryFindByValue(value, out _);
        }

        // The storage id of a value that is either an id already or a registered value.
        public string NormalizeItem(object? value)
        {
            if (value is string id)
            {
                if (this.ById.ContainsKey(id))
                {
                    return id;
                }

                throw new InvalidOperationException("Stored value token is not part of allowed values.");
            }

            if (value is not null && this.TryFindByValue(value, out var entry))
            {
                return entry.Id;
            }

            throw new InvalidOperationException("Value type mismatch for allowed values.");
        }

        private static bool IsNumber(object value) => NumericTypes.Contains(value.GetType());

        // Converts a number to a registered value type only when nothing is lost (2.5 never becomes 2).
        private static bool TryConvertExactly(object number, Type targetType, out object converted)
        {
            var numericType = targetType.IsEnum ? Enum.GetUnderlyingType(targetType) : targetType;
            try
            {
                var candidate = Convert.ChangeType(number, numericType, CultureInfo.InvariantCulture);
                if (IsNumber(candidate)
                    && Convert.ToDecimal(candidate, CultureInfo.InvariantCulture) == Convert.ToDecimal(number, CultureInfo.InvariantCulture))
                {
                    converted = targetType.IsEnum ? Enum.ToObject(targetType, candidate) : candidate;
                    return true;
                }
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                // Not representable in the target type.
            }

            converted = null!;
            return false;
        }

        private bool TryFindByValue(object value, out KVExplicitAllowedValue entry)
        {
            if (this.ByValue.TryGetValue(value, out entry))
            {
                return true;
            }

            if (IsNumber(value))
            {
                foreach (var valueType in this.ValueTypes)
                {
                    if (TryConvertExactly(value, valueType, out var converted) && this.ByValue.TryGetValue(converted, out entry))
                    {
                        return true;
                    }
                }
            }

            entry = default;
            return false;
        }

        public object? Denormalize(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is string id && this.ById.TryGetValue(id, out var entry))
            {
                return entry.Value;
            }

            throw new InvalidOperationException("Stored value token is not part of allowed values.");
        }
    }
}
