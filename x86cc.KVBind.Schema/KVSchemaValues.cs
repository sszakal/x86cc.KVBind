using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace x86cc.KVBind.Schema;

// .NET type -> KVSchemaValueKind, and value -> invariant text. Kept separate from the mapper so a client
// that builds or reads schema values (e.g. a .NET UI) can use the same rules.
public static class KVSchemaValues
{
    private static readonly Dictionary<Type, KVSchemaValueKind> Kinds = new()
    {
        [typeof(string)] = KVSchemaValueKind.String,
        [typeof(char)] = KVSchemaValueKind.String,
        [typeof(bool)] = KVSchemaValueKind.Boolean,
        [typeof(sbyte)] = KVSchemaValueKind.Integer,
        [typeof(byte)] = KVSchemaValueKind.Integer,
        [typeof(short)] = KVSchemaValueKind.Integer,
        [typeof(ushort)] = KVSchemaValueKind.Integer,
        [typeof(int)] = KVSchemaValueKind.Integer,
        [typeof(uint)] = KVSchemaValueKind.Integer,
        [typeof(long)] = KVSchemaValueKind.Integer,
        [typeof(ulong)] = KVSchemaValueKind.Integer,
        [typeof(decimal)] = KVSchemaValueKind.Decimal,
        [typeof(float)] = KVSchemaValueKind.Number,
        [typeof(double)] = KVSchemaValueKind.Number,
        [typeof(DateOnly)] = KVSchemaValueKind.Date,
        [typeof(DateTime)] = KVSchemaValueKind.DateTime,
        [typeof(DateTimeOffset)] = KVSchemaValueKind.DateTime,
        [typeof(TimeOnly)] = KVSchemaValueKind.Time,
        [typeof(TimeSpan)] = KVSchemaValueKind.Duration,
        [typeof(Guid)] = KVSchemaValueKind.Guid,
    };

    /// <summary>The kind of a scalar type (nullable wrappers are unwrapped).</summary>
    public static KVSchemaValueKind KindOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsEnum)
        {
            return KVSchemaValueKind.Enum;
        }

        return Kinds.TryGetValue(underlying, out var kind) ? kind : KVSchemaValueKind.Object;
    }

    /// <summary>The element type of an array or list type (not string), otherwise null.</summary>
    public static Type? ElementTypeOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        foreach (var candidate in (Type[])[type, .. type.GetInterfaces()])
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>True when the value is a scalar with a defined text form (not a sequence or an object).</summary>
    public static bool IsScalar(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return KindOf(value.GetType()) != KVSchemaValueKind.Object;
    }

    /// <summary>Invariant text of a scalar: ISO 8601 for dates and times, the member name for enums,
    /// round-trippable numbers; null for null.</summary>
    public static string? ToText(object? value)
    {
        return value switch
        {
            null => null,
            string text => text,
            bool flag => flag ? "true" : "false",
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
            TimeSpan duration => duration.ToString("c", CultureInfo.InvariantCulture),
            Guid guid => guid.ToString("D"),
            Enum member => member.ToString(),
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            float number => number.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The elements of a sequence value as invariant text (a string is not a sequence).</summary>
    public static List<string> ToTexts(object? value)
    {
        var texts = new List<string>();
        if (value is IEnumerable sequence and not string)
        {
            foreach (var item in sequence)
            {
                texts.Add(ToText(item) ?? string.Empty);
            }
        }

        return texts;
    }
}
