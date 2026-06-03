using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace x86cc.KVBind.Core.Model;

public sealed class KVValueJsonConverter : JsonConverter<KVValue>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override KVValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.StartObject => ReadObject(ref reader),
            JsonTokenType.String => new KVValue<string?>(reader.GetString()),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.True => new KVValue<bool>(true),
            JsonTokenType.False => new KVValue<bool>(false),
            JsonTokenType.Null => new KVValue<object?>(null),
            JsonTokenType.StartArray => ReadJsonValue(ref reader),
            _ => throw new JsonException($"Unsupported KV stored value token '{reader.TokenType}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, KVValue value, JsonSerializerOptions options)
    {
        var valueType = GetValueType(value);
        writer.WriteStartObject();
        writer.WriteString("$type", valueType.AssemblyQualifiedName);
        writer.WritePropertyName("value");
        if (value.Value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(writer, value.Value, valueType, options);
        }
        writer.WriteEndObject();
    }

    private static KVValue ReadObject(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (TryGetProperty(root, "$type", out var typeElement))
        {
            var typeName = typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
            var valueType = !string.IsNullOrWhiteSpace(typeName) ? Type.GetType(typeName, throwOnError: false) : typeof(object);
            if (valueType is null)
            {
                throw new JsonException($"Unable to resolve KV value type '{typeName}'.");
            }

            object? value = null;
            if (TryGetProperty(root, "value", out var valueElement) && valueElement.ValueKind != JsonValueKind.Null)
            {
                value = valueElement.Deserialize(valueType, JsonOptions);
            }

            return Create(valueType, value);
        }
        
        return new KVValue<string>(root.GetRawText());
    }
    
    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static KVValue ReadNumber(ref Utf8JsonReader reader)
    {
        if (reader.TryGetInt32(out var intValue))
        {
            return new KVValue<int>(intValue);
        }

        if (reader.TryGetInt64(out var longValue))
        {
            return new KVValue<long>(longValue);
        }

        return new KVValue<decimal>(reader.GetDecimal());
    }

    private static KVValue ReadJsonValue(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return new KVValue<string>(document.RootElement.GetRawText());
    }

    private static KVValue Create(Type valueType, object? value)
    {
        var wrapperType = typeof(KVValue<>).MakeGenericType(valueType);
        return (KVValue)Activator.CreateInstance(wrapperType, value)!;
    }

    private static Type GetValueType(KVValue value)
    {
        var type = value.GetType();
        while (type is not null)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KVValue<>))
            {
                return type.GetGenericArguments()[0];
            }

            type = type.BaseType;
        }

        return value.Value?.GetType() ?? typeof(object);
    }
}
