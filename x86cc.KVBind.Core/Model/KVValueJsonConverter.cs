using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace x86cc.KVBind.Core.Model;

public sealed class KVValueJsonConverter : JsonConverter<KVValue>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override KVValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"KVValue must be a JSON object, got '{reader.TokenType}'.");
        return ReadObject(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, KVValue value, JsonSerializerOptions options)
    {
        if (value == KVValue.Tombstone)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("$tombstone", true);
            writer.WriteEndObject();
            return;
        }

        var valueType = GetValueType(value);
        writer.WriteStartObject();
        writer.WriteString("$type", valueType.AssemblyQualifiedName);
        writer.WritePropertyName("value");
        if (value.Value is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Value, valueType, options);
        writer.WriteEndObject();
    }

    private static KVValue ReadObject(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (TryGetProperty(root, "$tombstone", out var tombstoneElement) && tombstoneElement.ValueKind == JsonValueKind.True)
            return KVValue.Tombstone;

        if (TryGetProperty(root, "$type", out var typeElement))
        {
            var typeName = typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
            var valueType = !string.IsNullOrWhiteSpace(typeName) ? Type.GetType(typeName, throwOnError: false) : typeof(object);
            if (valueType is null)
                throw new JsonException($"Unable to resolve KV value type '{typeName}'.");

            object? value = null;
            if (TryGetProperty(root, "value", out var valueElement) && valueElement.ValueKind != JsonValueKind.Null)
                value = valueElement.Deserialize(valueType, JsonOptions);

            return Create(valueType, value);
        }

        throw new JsonException("KVValue object must have '$tombstone' or '$type' property.");
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
                return type.GetGenericArguments()[0];
            type = type.BaseType;
        }

        return value.Value?.GetType() ?? typeof(object);
    }
}
