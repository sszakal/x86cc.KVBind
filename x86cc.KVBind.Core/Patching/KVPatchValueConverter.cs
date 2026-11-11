using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace x86cc.KVBind.Core;

// Converts the value of a SET patch to the declared type of its field, so a patch can never store a value
// the typed property cannot read back. Patch values usually arrive from JSON, where a decimal field may be
// sent as an integer, a number in a string, or a JsonElement; lossy or meaningless conversions (1.5 into an
// int, text into a number, a number into a string) are rejected.
internal static class KVPatchValueConverter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    internal static object ConvertToFieldType(object value, Type fieldType, string fieldPath)
    {
        var targetType = Nullable.GetUnderlyingType(fieldType) ?? fieldType;
        if (targetType == typeof(object) || targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is JsonElement element)
        {
            if (TryDeserialize(element.GetRawText(), targetType, out var fromElement)) return fromElement;
        }
        else
        {
            if (TrySerialize(value, out var json) && TryDeserialize(json, targetType, out var converted)) return converted;

            // A string may also carry raw JSON for a structured field, e.g. "[1,2]" for an int[].
            if (value is string text && TryDeserialize(text, targetType, out var fromText)) return fromText;
        }

        throw new InvalidOperationException(
            $"Value of type '{DescribeType(value)}' for field '{fieldPath}' cannot be converted to '{targetType.FullName}'.");
    }

    private static string DescribeType(object value)
        => value is JsonElement element ? $"JSON {element.ValueKind}" : value.GetType().FullName!;

    private static bool TrySerialize(object value, out string json)
    {
        try
        {
            json = JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
            return true;
        }
        catch (NotSupportedException)
        {
            json = string.Empty;
            return false;
        }
    }

    private static bool TryDeserialize(string json, Type targetType, out object value)
    {
        try
        {
            var result = JsonSerializer.Deserialize(json, targetType, JsonOptions);
            value = result!;
            return result is not null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
        {
            value = null!;
            return false;
        }
    }
}
