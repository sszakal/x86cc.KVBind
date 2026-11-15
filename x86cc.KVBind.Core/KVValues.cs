using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

// Small rules about values that validation, allowed values and patching share, kept in one place so they
// cannot drift apart.
internal static class KVValues
{
    private static readonly HashSet<Type> NumericTypes =
    [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
    ];

    // Null or blank text: nothing was entered. Value rules skip it; "required" reports it.
    internal static bool IsUnset(object? value) => value is null || (value is string text && string.IsNullOrWhiteSpace(text));

    internal static bool IsNumber(object value) => NumericTypes.Contains(value.GetType());

    // A JSON number as a CLR value: long when integral (2, and 2.0 too), decimal when it fits, otherwise
    // double (1e30 is a valid JSON number).
    internal static object ReadJsonNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        if (!element.TryGetDecimal(out var decimalValue))
        {
            return element.GetDouble();
        }

        return decimalValue == decimal.Truncate(decimalValue) && decimalValue is >= long.MinValue and <= long.MaxValue
            ? (long)decimalValue
            : decimalValue;
    }

    // Equality of two stored values by content: sequences (arrays, lists) element by element, as KVValue
    // compares them, where object.Equals would compare references.
    internal static bool AreEqual(object? left, object? right)
    {
        if (Equals(left, right))
        {
            return true;
        }

        return left is IEnumerable and not string && right is IEnumerable and not string
               && KVValue.FromObject(left).Equals(KVValue.FromObject(right));
    }
}
