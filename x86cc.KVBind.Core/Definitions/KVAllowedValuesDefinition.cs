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

/// <summary>One allowed value as declared in the DSL: the stored id, its label, and for an allowed-value
/// component the display template and the placeholders it asks for.</summary>
public sealed record KVAllowedValueInfo(
    string Id,
    string Label,
    string? Template,
    IReadOnlyList<KVAllowedValuePlaceholderInfo> Placeholders);

/// <summary>A parameter of an allowed-value component (e.g. a deductible amount).</summary>
public sealed record KVAllowedValuePlaceholderInfo(string Name, string Label, Type ValueType, bool IsRequired);

internal sealed class KVAllowedValuesDefinition
{
    private readonly Func<object?, bool> isAllowed;
    private readonly Func<object?, object?> normalizeForStorage;
    private readonly Func<object?, object?> denormalizeFromStorage;
    private readonly Resolver resolve;

    private KVAllowedValuesDefinition(
        Func<object?, bool> isAllowed,
        Func<object?, object?> normalizeForStorage,
        Func<object?, object?> denormalizeFromStorage,
        Resolver? resolve = null)
    {
        this.isAllowed = isAllowed;
        this.normalizeForStorage = normalizeForStorage;
        this.denormalizeFromStorage = denormalizeFromStorage;
        this.resolve = resolve ?? ((object _, out object? resolved) =>
        {
            resolved = null;
            return false;
        });
    }

    // Reads an incoming value (a typed value, or a token as a client sends it) as one of the allowed values.
    private delegate bool Resolver(object value, out object? resolved);

    // The declared values, in declaration order, for describing the field (e.g. a schema for a UI).
    public IReadOnlyList<KVAllowedValueInfo> Options { get; private init; } = [];

    // True when the field is a sequence and each element must be one of the options.
    public bool IsElementWise { get; private init; }

    // True when the tokens are text, so an unknown text value can be kept in a draft for validation to report
    // (and read back as text). For other token types an unknown value has no readable form and is rejected.
    public bool KeepsUnknownTokens { get; private init; }

    public static KVAllowedValuesDefinition Create<TValue>(IEnumerable<TValue> values)
    {
        var declared = values.ToArray();
        var allowedSet = new HashSet<TValue>(declared);
        return new KVAllowedValuesDefinition(
            isAllowed: value => value is TValue typed && allowedSet.Contains(typed),
            normalizeForStorage: value => value,
            denormalizeFromStorage: value => value,
            resolve: (object value, out object? resolved) =>
            {
                // The values are their own tokens: match the value as the field's type ("2" or 2.0 for 2).
                var found = TryAs<TValue>(value, out var typed) && allowedSet.Contains(typed);
                resolved = found ? typed : null;
                return found;
            })
        {
            Options = DescribeTokens(declared, value => value!, labelSelector: null),
            KeepsUnknownTokens = typeof(TValue) == typeof(string),
        };
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
            isAllowed: value => (value is TValue typed && valueToToken.ContainsKey(typed))
                              || (value is TToken token && tokenToValue.ContainsKey(token)),
            normalizeForStorage: value => value is TValue typed
                ? valueToToken[typed]
                : throw new InvalidOperationException("Value type mismatch for allowed values."),
            denormalizeFromStorage: value => value is TToken token && tokenToValue.TryGetValue(token, out var typed)
                ? typed
                : throw new InvalidOperationException("Stored value token is not part of allowed values."),
            resolve: (object value, out object? resolved) =>
            {
                // The token first: tokens are the wire form, so 10 means the value whose token is 10, even when
                // 10 is also an enum member's number or another allowed value. Then the value as the field's type.
                if (TryAs<TToken>(value, out var token) && tokenToValue.TryGetValue(token, out var byToken))
                {
                    resolved = byToken;
                    return true;
                }

                var found = TryAs<TValue>(value, out var converted) && valueToToken.ContainsKey(converted!);
                resolved = found ? converted : null;
                return found;
            })
        {
            Options = DescribeTokens(tokenToValue.Values, value => tokenSelector(value), labelSelector),
            KeepsUnknownTokens = typeof(TToken) == typeof(string),
        };
    }

    public static KVAllowedValuesDefinition CreateExplicit(IReadOnlyCollection<KVExplicitAllowedValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var index = ExplicitValueIndex.Build(values);
        return new KVAllowedValuesDefinition(
            isAllowed: index.IsAllowed,
            normalizeForStorage: value => value is null ? null : index.NormalizeItem(value),
            denormalizeFromStorage: index.Denormalize,
            resolve: (object value, out object? resolved) =>
            {
                // By id or by value; read back as the entry's value.
                var found = index.IsAllowed(value);
                resolved = found ? index.Denormalize(index.NormalizeItem(value)) : null;
                return found;
            })
        {
            Options = DescribeExplicit(values),
            KeepsUnknownTokens = true,
        };
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

        var allowedElements = new HashSet<TElement>(tokenToValue.Values);
        return new KVAllowedValuesDefinition(
            isAllowed: value => TryReadSequence(value, out var items) && items.All(IsAllowedItem),
            normalizeForStorage: value => NormalizeSequence<TToken>(value, IsAllowedItem, NormalizeItem),
            denormalizeFromStorage: value => DenormalizeSequence(value, tokenToValue, allowedElements))
        {
            Options = DescribeTokens(tokenToValue.Values, value => tokenSelector(value), labelSelector),
            IsElementWise = true,
        };

        bool IsAllowedItem(object? value)
        {
            return (value is TElement typed && valueToToken.ContainsKey(typed))
                   || (TryConvertToken<TToken>(value, out var token) && tokenToValue.ContainsKey(token));
        }

        TToken NormalizeItem(object? value)
        {
            if (value is TElement typed)
            {
                return valueToToken[typed];
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

        var allowedExplicitElements = new HashSet<TElement>(elementsById.Values);
        return new KVAllowedValuesDefinition(
            isAllowed: value => TryReadSequence(value, out var items) && items.All(index.IsAllowed),
            normalizeForStorage: value => NormalizeSequence<string>(value, index.IsAllowed, index.NormalizeItem),
            denormalizeFromStorage: value => DenormalizeSequence(value, elementsById, allowedExplicitElements))
        {
            Options = DescribeExplicit(values),
            IsElementWise = true,
        };
    }

    public bool IsAllowed(object? value)
    {
        return this.isAllowed(value);
    }

    // The allowed value an incoming single value stands for, or false when it is none of them.
    public bool TryResolve(object value, out object? resolved) => this.resolve(value, out resolved);

    public object? NormalizeForStorage(object? value)
    {
        return this.normalizeForStorage(value);
    }

    public object? DenormalizeFromStorage(object? value)
    {
        return this.denormalizeFromStorage(value);
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

    // The value as T: itself, or converted losslessly (a JSON number or numeric text for a number, a member
    // name for an enum).
    private static bool TryAs<T>(object value, out T typed)
    {
        if (value is T same)
        {
            typed = same;
            return true;
        }

        if (KVPatchValueConverter.TryConvertToFieldType(value, typeof(T), out var converted) && converted is T result)
        {
            typed = result;
            return true;
        }

        typed = default!;
        return false;
    }

    // Explicit values, in declaration order, skipping entries without an id (the index ignores them too).
    private static KVAllowedValueInfo[] DescribeExplicit(IReadOnlyCollection<KVExplicitAllowedValue> values)
    {
        return values
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Id))
            .Select(static entry => new KVAllowedValueInfo(
                entry.Id,
                entry.Label,
                entry.Template,
                (entry.Placeholders ?? [])
                    .GroupBy(static p => p.Name, StringComparer.Ordinal)
                    .Select(static g => g.First())
                    .Select(static p => new KVAllowedValuePlaceholderInfo(p.Name, p.Label, p.ValueType, p.IsRequired))
                    .ToArray()))
            .ToArray();
    }

    // Token-based values: the id is the token's invariant text, the label falls back to the id.
    private static KVAllowedValueInfo[] DescribeTokens<TValue>(
        IEnumerable<TValue> values,
        Func<TValue, object> tokenSelector,
        Func<TValue, string>? labelSelector)
    {
        return values
            .Select(value =>
            {
                var id = Convert.ToString(tokenSelector(value), CultureInfo.InvariantCulture) ?? string.Empty;
                return new KVAllowedValueInfo(id, labelSelector?.Invoke(value) ?? id, Template: null, Placeholders: []);
            })
            .ToArray();
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

    // allowedElements is built once per definition (not per read): an item that is already an element is
    // only accepted when it is one of them; for a string element type every unknown token would otherwise
    // pass as an "element".
    private static object? DenormalizeSequence<TToken, TElement>(
        object? value,
        IReadOnlyDictionary<TToken, TElement> tokenToValue,
        HashSet<TElement> allowedElements)
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

            if (item is TElement element && allowedElements.Contains(element))
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
                JsonValueKind.Number => KVValues.ReadJsonNumber(element),
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
                // Not convertible to the token type: treated as no match below.
            }
            catch (FormatException)
            {
                // Not convertible to the token type: treated as no match below.
            }
            catch (OverflowException)
            {
                // Not convertible to the token type: treated as no match below.
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

        // Converts a number to a registered value type only when nothing is lost (2.5 never becomes 2).
        private static bool TryConvertExactly(object number, Type targetType, out object converted)
        {
            var numericType = targetType.IsEnum ? Enum.GetUnderlyingType(targetType) : targetType;
            try
            {
                var candidate = Convert.ChangeType(number, numericType, CultureInfo.InvariantCulture);
                if (KVValues.IsNumber(candidate)
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

            if (KVValues.IsNumber(value))
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
    }
}
