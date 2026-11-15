using System.Collections.Generic;

namespace x86cc.KVBind.Schema;

// The schema model is deliberately plain: classes with a parameterless constructor and settable members of
// type string, bool, int, decimal, enums, lists and nested schema classes. No object-typed members, no
// dictionaries and no serializer attributes, so JSON, MessagePack, Protobuf-style or XML serializers can all
// handle it with their defaults. Values (defaults, bounds, annotations) are carried as invariant text and
// read according to the owning member's KVSchemaValueKind.

/// <summary>The description of one KVBind model (an aggregate root definition).</summary>
public sealed class KVSchemaDocument
{
    /// <summary>The model's name, e.g. the root node's CLR type name.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>The newest schema (migration) version the definition declares; 0 without migrations.</summary>
    public int SchemaVersion { get; set; }

    public KVNodeSchema Root { get; set; } = new();
}

/// <summary>A field group, a collection item type, a nested node type, or the root.</summary>
public sealed class KVNodeSchema
{
    /// <summary>The canonical key of the node relative to its parent; empty for the root and for item and
    /// nested types (their slot carries the key).</summary>
    public string Key { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsInherited { get; set; }

    /// <summary>True when the group accepts a <c>RESET</c> patch.</summary>
    public bool IsResettable { get; set; }

    public List<KVFieldSchema> Fields { get; set; } = [];

    public List<KVNodeSchema> Groups { get; set; } = [];

    public List<KVCollectionSchema> Collections { get; set; } = [];

    public List<KVNestedNodeSchema> NestedNodes { get; set; } = [];

    /// <summary>The custom patch operations addressed to this node, in declaration order.</summary>
    public List<string> Operations { get; set; } = [];

    public List<KVAnnotationSchema> Annotations { get; set; } = [];
}

public sealed class KVFieldSchema
{
    public string Key { get; set; } = string.Empty;

    public string? Label { get; set; }

    /// <summary>The kind of the value, or of each element when <see cref="IsArray"/>.</summary>
    public KVSchemaValueKind Kind { get; set; }

    /// <summary>True for array and list fields.</summary>
    public bool IsArray { get; set; }

    public bool IsNullable { get; set; }

    /// <summary>The member names when <see cref="Kind"/> is <see cref="KVSchemaValueKind.Enum"/>.</summary>
    public List<string> EnumMembers { get; set; } = [];

    /// <summary>True for a [Flags] enum (values may combine members).</summary>
    public bool IsFlags { get; set; }

    public bool IsRequired { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsInherited { get; set; }

    public bool HasDefault { get; set; }

    /// <summary>The default as invariant text; for an array field, see <see cref="DefaultValues"/>.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>The default elements of an array field.</summary>
    public List<string> DefaultValues { get; set; } = [];

    public int? MinLength { get; set; }

    public int? MaxLength { get; set; }

    /// <summary>A regular expression the whole value must match.</summary>
    public string? Pattern { get; set; }

    /// <summary>Inclusive lower bound as invariant text.</summary>
    public string? Min { get; set; }

    /// <summary>Inclusive upper bound as invariant text.</summary>
    public string? Max { get; set; }

    /// <summary>The declared values, or null when the field accepts any value.</summary>
    public KVAllowedValuesSchema? AllowedValues { get; set; }

    public List<KVAnnotationSchema> Annotations { get; set; } = [];
}

public sealed class KVAllowedValuesSchema
{
    /// <summary>True when each element of an array field must be one of <see cref="Options"/>.</summary>
    public bool AppliesToElements { get; set; }

    public List<KVAllowedValueSchema> Options { get; set; } = [];
}

public sealed class KVAllowedValueSchema
{
    /// <summary>The value stored and sent in patches.</summary>
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>For an allowed-value component, a display template such as <c>"Collision — {Deductible:C}"</c>.</summary>
    public string? Template { get; set; }

    public List<KVPlaceholderSchema> Placeholders { get; set; } = [];
}

public sealed class KVPlaceholderSchema
{
    public string Name { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public KVSchemaValueKind Kind { get; set; }

    public bool IsRequired { get; set; }
}

public sealed class KVCollectionSchema
{
    public string Key { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsInherited { get; set; }

    public bool NotEmpty { get; set; }

    public int? MinCount { get; set; }

    public int? MaxCount { get; set; }

    /// <summary>The structural patch operations clients may apply (ADD, REMOVE, MOVE, RESET and custom
    /// operations); null when every operation is allowed.</summary>
    public List<string>? AllowedOperations { get; set; }

    /// <summary>Custom patch operations registered on the collection itself.</summary>
    public List<string> Operations { get; set; } = [];

    public List<KVItemTypeSchema> ItemTypes { get; set; } = [];

    /// <summary>Items created when the collection is born.</summary>
    public List<KVSeedSchema> Seeds { get; set; } = [];

    public List<KVAggregateRuleSchema> AggregateRules { get; set; } = [];

    public List<KVAnnotationSchema> Annotations { get; set; } = [];
}

/// <summary>A collection item type or a nested node type, identified by its type token.</summary>
/// <remarks>
/// Definitions may be recursive (a nested node type whose items hold that type again, so the tree can grow
/// without limit). The schema stays finite: the first occurrence of such a type carries an <see cref="Id"/>,
/// and an occurrence inside itself carries a <see cref="Ref"/> to that id instead of a node.
/// <see cref="KVSchemaReferences"/> resolves them.
/// </remarks>
public sealed class KVItemTypeSchema
{
    public string Token { get; set; } = string.Empty;

    public string? Label { get; set; }

    /// <summary>Set on a type that occurs again inside itself; the id its references use. Null otherwise.</summary>
    public string? Id { get; set; }

    /// <summary>Set when this occurrence refers back to an enclosing type (the one whose <see cref="Id"/> it
    /// names); <see cref="Node"/> is then empty. Null otherwise.</summary>
    public string? Ref { get; set; }

    /// <summary>The type's node; empty when <see cref="Ref"/> is set.</summary>
    public KVNodeSchema Node { get; set; } = new();
}

public sealed class KVSeedSchema
{
    public string ItemId { get; set; } = string.Empty;

    public string TypeToken { get; set; } = string.Empty;
}

public sealed class KVAggregateRuleSchema
{
    /// <summary>The item field that is summed.</summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>LessThan, LessThanOrEqual, GreaterThan or GreaterThanOrEqual.</summary>
    public string Comparison { get; set; } = string.Empty;

    public decimal Threshold { get; set; }

    public string ErrorCode { get; set; } = string.Empty;
}

public sealed class KVNestedNodeSchema
{
    public string Key { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsInherited { get; set; }

    /// <summary>True when validation requires a type to be initialized.</summary>
    public bool IsRequired { get; set; }

    /// <summary>The type initialized when the slot is born, if any.</summary>
    public string? DefaultType { get; set; }

    public List<KVItemTypeSchema> Types { get; set; } = [];

    public List<KVAnnotationSchema> Annotations { get; set; } = [];
}

/// <summary>An application annotation from the DSL (e.g. a UI hint). Only scalar values are carried.</summary>
public sealed class KVAnnotationSchema
{
    public string Key { get; set; } = string.Empty;

    /// <summary>The value as invariant text, read according to <see cref="Kind"/>; null for a null value.</summary>
    public string? Value { get; set; }

    public KVSchemaValueKind Kind { get; set; }
}
