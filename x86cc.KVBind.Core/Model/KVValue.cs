using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace x86cc.KVBind.Core.Model;

[JsonConverter(typeof(KVValueJsonConverter))]
public abstract class KVValue : IEquatable<KVValue>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public abstract object? Value { get; }

    public static implicit operator KVValue(string? value) => new KVValue<string?>(value);

    public static KVValue FromObject(object? value)
    {
        if (value is KVValue storedValue)
        {
            return storedValue;
        }

        var valueType = value?.GetType() ?? typeof(object);
        var wrapperType = typeof(KVValue<>).MakeGenericType(valueType);
        return (KVValue)Activator.CreateInstance(wrapperType, value)!;
    }

    public bool Equals(KVValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (Value is null || other.Value is null)
        {
            return Value is null && other.Value is null;
        }

        return Value.GetType() == other.Value.GetType()
               && string.Equals(JsonSerializer.Serialize(Value, Value.GetType(), JsonOptions), JsonSerializer.Serialize(other.Value, other.Value.GetType(), JsonOptions), StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is KVValue other ? Equals(other) : Equals(Value, obj);
    }

    public override int GetHashCode()
    {
        return Value is null ? 0 : HashCode.Combine(Value.GetType(), JsonSerializer.Serialize(Value, Value.GetType(), JsonOptions));
    }

    public override string? ToString() => Value?.ToString();
}

public sealed class KVValue<T>(T? value) : KVValue
{
    public T? TypedValue { get; } = value;

    public override object? Value => TypedValue;
}
