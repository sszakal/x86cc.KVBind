using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace x86cc.KVBind.Core.Model;

public sealed class KVValueJsonConverter : JsonConverter<KVValue>
{
    // The most type names ResolvedTypes holds; beyond it, names are resolved uncached.
    private const int MaxResolvedTypes = 1024;

    // Values are written with the caller's options, so an enum arrives as its name when the host serializes
    // enums as strings (e.g. Marten with EnumStorage.AsString) and as a number otherwise; read both.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Strips ", Version=X, Culture=neutral, PublicKeyToken=X" from assembly-qualified type names.
    // Works for generic types too since the same pattern repeats for each type argument.
    // "System.String, System.Private.CoreLib" is sufficient for Type.GetType() to resolve correctly.
    private static readonly Regex VersionSuffix = new(
        @", Version=[^,\[\]]+, Culture=[^,\[\]]+, PublicKeyToken=[^,\[\]]+",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    // Resolving an assembly-qualified name is costly and every stored value carries one; there are few
    // distinct value types, so each name is resolved once. Only names that resolve are cached: a type whose
    // assembly loads later still resolves then, and unknown names in incoming documents cannot grow the cache.
    private static readonly ConcurrentDictionary<string, Type> ResolvedTypes = new(StringComparer.Ordinal);

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
        writer.WriteString("$type", ShortTypeName(valueType));
        writer.WritePropertyName("value");
        if (value.Value is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Value, valueType, options);
        writer.WriteEndObject();
    }

    // For tests: whether a type name is in the resolution cache.
    internal static bool IsCached(string typeName) => ResolvedTypes.ContainsKey(typeName);

    private static string ShortTypeName(Type type) =>
        VersionSuffix.Replace(type.AssemblyQualifiedName!, string.Empty);

    private static Type? ResolveType(string typeName)
    {
        if (ResolvedTypes.TryGetValue(typeName, out var cached))
        {
            return cached;
        }

        var resolved = Type.GetType(typeName, throwOnError: false);
        if (resolved is not null && ResolvedTypes.Count < MaxResolvedTypes)
        {
            ResolvedTypes.TryAdd(typeName, resolved);
        }

        return resolved;
    }

    private static KVValue ReadObject(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (TryGetProperty(root, "$tombstone", out var tombstoneElement) && tombstoneElement.ValueKind == JsonValueKind.True)
            return KVValue.Tombstone;

        if (TryGetProperty(root, "$type", out var typeElement))
            return ReadTypedValue(root, typeElement);

        throw new JsonException("KVValue object must have '$tombstone' or '$type' property.");
    }

    private static KVValue ReadTypedValue(JsonElement root, JsonElement typeElement)
    {
        var typeName = typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;
        var valueType = !string.IsNullOrWhiteSpace(typeName)
            ? ResolveType(typeName)
            : typeof(object);
        if (valueType is null)
            throw new JsonException($"Unable to resolve KV value type '{typeName}'.");

        object? value = null;
        if (TryGetProperty(root, "value", out var valueElement) && valueElement.ValueKind != JsonValueKind.Null)
            value = valueElement.Deserialize(valueType, JsonOptions);

        return Create(valueType, value);
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
