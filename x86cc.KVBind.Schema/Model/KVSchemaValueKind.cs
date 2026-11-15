namespace x86cc.KVBind.Schema;

/// <summary>The kind of a value, independent of .NET types.</summary>
public enum KVSchemaValueKind
{
    /// <summary>Anything that does not map to a more specific kind.</summary>
    Object,
    String,
    Boolean,

    /// <summary>Whole numbers (byte through long; ulong too).</summary>
    Integer,

    /// <summary>Exact decimal numbers.</summary>
    Decimal,

    /// <summary>Floating-point numbers (float, double).</summary>
    Number,

    /// <summary>A calendar date, text in <c>yyyy-MM-dd</c>.</summary>
    Date,

    /// <summary>A date and time, text in ISO 8601 round-trip format.</summary>
    DateTime,

    /// <summary>A time of day, text in <c>HH:mm:ss.FFFFFFF</c>.</summary>
    Time,

    /// <summary>A duration, text in the invariant <c>c</c> format.</summary>
    Duration,

    Guid,

    /// <summary>One of <see cref="KVFieldSchema.EnumMembers"/> (by name).</summary>
    Enum,
}
