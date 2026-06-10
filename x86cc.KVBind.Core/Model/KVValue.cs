using System;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace x86cc.KVBind.Core.Model;

[JsonConverter(typeof(KVValueJsonConverter))]
public abstract class KVValue : IEquatable<KVValue>
{
    // Sentinel representing a deleted path — used instead of a separate Removed set.
    public static readonly KVValue Tombstone = new KVTombstoneValue();

    private sealed class KVTombstoneValue : KVValue
    {
        public override object? Value => null;
        public override bool Equals(object? obj) => obj is KVTombstoneValue;
        public override int GetHashCode() => nameof(KVTombstoneValue).GetHashCode(StringComparison.Ordinal);
        public override string ToString() => "(deleted)";
    }


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

        // Same-runtime-type rule (unchanged), then structural equality on the wrapped values.
        return Value.GetType() == other.Value.GetType()
               && StructuralEquals(Value, other.Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is KVValue other ? Equals(other) : Equals(Value, obj);
    }

    public override int GetHashCode()
    {
        return Value is null ? 0 : HashCode.Combine(Value.GetType(), StructuralHash(Value));
    }

    public override string? ToString() => Value?.ToString();

    // Structural equality of two wrapped values known to share a runtime type. Strings and value types
    // use their own (value) equality; arrays/collections compare element-wise; reference-type POCOs fall
    // back to the original JSON-string compare so nothing exotic regresses.
    private static bool StructuralEquals(object a, object b)
    {
        switch (a)
        {
            case string sa:
                return string.Equals(sa, (string)b, StringComparison.Ordinal);

            case IEnumerable ea:
                var eb = (IEnumerable)b;
                var ia = ea.GetEnumerator();
                var ib = eb.GetEnumerator();
                try
                {
                    while (true)
                    {
                        var hasA = ia.MoveNext();
                        var hasB = ib.MoveNext();
                        if (hasA != hasB) return false;          // different lengths
                        if (!hasA) return true;                  // both exhausted
                        if (!ElementEquals(ia.Current, ib.Current)) return false;
                    }
                }
                finally
                {
                    (ia as IDisposable)?.Dispose();
                    (ib as IDisposable)?.Dispose();
                }

            default:
                return a.GetType().IsValueType
                    ? a.Equals(b)
                    : JsonEquals(a, b);
        }
    }

    private static bool ElementEquals(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.GetType() == b.GetType() && StructuralEquals(a, b);
    }

    private static bool JsonEquals(object a, object b) =>
        string.Equals(
            JsonSerializer.Serialize(a, a.GetType(), JsonOptions),
            JsonSerializer.Serialize(b, b.GetType(), JsonOptions),
            StringComparison.Ordinal);

    // Hash consistent with StructuralEquals: equal values always produce equal hashes.
    private static int StructuralHash(object v)
    {
        switch (v)
        {
            case string s:
                return s.GetHashCode(StringComparison.Ordinal);

            case IEnumerable e:
                var hash = new HashCode();
                foreach (var element in e)
                    hash.Add(element is null ? 0 : StructuralHash(element));
                return hash.ToHashCode();

            default:
                return v.GetType().IsValueType
                    ? v.GetHashCode()
                    : JsonSerializer.Serialize(v, v.GetType(), JsonOptions).GetHashCode(StringComparison.Ordinal);
        }
    }
}

public sealed class KVValue<T>(T? value) : KVValue
{
    // Box value types once at construction so repeated Value reads (the field-read hot path)
    // return the same boxed reference instead of re-boxing on every access.
    private readonly object? _boxed = value;

    public T? TypedValue { get; } = value;

    public override object? Value => _boxed;
}
