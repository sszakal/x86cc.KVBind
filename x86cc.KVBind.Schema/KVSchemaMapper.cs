using System;
using System.Collections.Generic;
using System.Linq;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.Schema;

/// <summary>
/// Maps a KVBind definition to its <see cref="KVSchemaDocument"/>. The result depends only on the definition,
/// so build it once per model and reuse it (definitions are immutable after the builder runs).
/// </summary>
/// <remarks>
/// Covers what the definition declares statically: structure, labels, value kinds, defaults, always-applied
/// constraints, allowed values, read-only / inherited / resettable flags, collection rules, seeds and
/// operations, and scalar annotations. Validation profile and group rules are compiled code and are not
/// described; clients still validate through the model (e.g. <c>root.Validate()</c>).
/// </remarks>
public static class KVSchemaMapper
{
    /// <summary>Maps the definition registered for <typeparamref name="TModel"/>; the model name is the type name.</summary>
    public static KVSchemaDocument Map<TModel>(IKVDefinitionRegistry registry)
        where TModel : KVRootNode
    {
        ArgumentNullException.ThrowIfNull(registry);
        return Map(registry.Get<TModel>(), typeof(TModel).Name);
    }

    public static KVSchemaDocument Map(KVNodeDefinition definition, string modelName)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        var root = new Mapping().MapNode(definition);
        root.Key = string.Empty;
        return new KVSchemaDocument
        {
            Model = modelName,
            SchemaVersion = definition.CurrentSchemaVersion,
            Root = root,
        };
    }

    // The state of one Map call: the item and nested types being mapped on the current branch, by
    // definition, to detect recursion, and the reference ids handed out.
    private sealed class Mapping
    {
        private readonly Dictionary<KVNodeDefinition, KVItemTypeSchema> open = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> ids = new(StringComparer.Ordinal);

        internal KVNodeSchema MapNode(KVNodeDefinition definition)
        {
            return new KVNodeSchema
            {
                Key = definition.SubSegmentPath,
                Label = definition.DisplayName,
                IsReadOnly = definition.IsPatchReadOnly,
                IsInherited = definition.IsInherited,
                IsResettable = definition.IsResettable == true,
                Fields = definition.Fields.Select(MapField).ToList(),
                Groups = definition.Nodes.Select(this.MapNode).ToList(),
                Collections = definition.Collections.Select(this.MapCollection).ToList(),
                NestedNodes = definition.NestedNodes.Select(this.MapNestedNode).ToList(),
                Operations = definition.OperationNames.ToList(),
                Annotations = MapAnnotations(definition),
            };
        }

        private static KVFieldSchema MapField(KVFieldDefinition field)
        {
            var elementType = KVSchemaValues.ElementTypeOf(field.ValueType);
            var scalarType = elementType ?? field.ValueType;
            var underlying = Nullable.GetUnderlyingType(scalarType) ?? scalarType;

            var schema = new KVFieldSchema
            {
                Key = field.SubSegmentPath,
                Label = field.DisplayName,
                Kind = KVSchemaValues.KindOf(scalarType),
                IsArray = elementType is not null,
                IsNullable = !field.ValueType.IsValueType || Nullable.GetUnderlyingType(field.ValueType) is not null,
                IsRequired = field.IsRequired,
                IsReadOnly = field.IsPatchReadOnly,
                IsInherited = field.IsInherited,
                HasDefault = field.HasDefault,
                MinLength = field.MinLength,
                MaxLength = field.MaxLength,
                Pattern = field.Pattern,
                Min = KVSchemaValues.ToText(field.Min),
                Max = KVSchemaValues.ToText(field.Max),
                AllowedValues = MapAllowedValues(field),
                Annotations = MapAnnotations(field),
            };

            if (underlying.IsEnum)
            {
                schema.EnumMembers = Enum.GetNames(underlying).ToList();
                schema.IsFlags = underlying.IsDefined(typeof(FlagsAttribute), inherit: false);
            }

            if (field.HasDefault)
            {
                if (schema.IsArray)
                {
                    schema.DefaultValues = KVSchemaValues.ToTexts(field.DefaultValue);
                }
                else
                {
                    schema.DefaultValue = KVSchemaValues.ToText(field.DefaultValue);
                }
            }

            return schema;
        }

        private static KVAllowedValuesSchema? MapAllowedValues(KVFieldDefinition field)
        {
            if (!field.HasAllowedValues)
            {
                return null;
            }

            return new KVAllowedValuesSchema
            {
                AppliesToElements = field.AllowedValuesApplyToElements,
                Options = field.AllowedValueOptions
                    .Select(static option => new KVAllowedValueSchema
                    {
                        Id = option.Id,
                        Label = option.Label,
                        Template = option.Template,
                        Placeholders = option.Placeholders
                            .Select(static placeholder => new KVPlaceholderSchema
                            {
                                Name = placeholder.Name,
                                Label = placeholder.Label,
                                Kind = KVSchemaValues.KindOf(placeholder.ValueType),
                                IsRequired = placeholder.IsRequired,
                            })
                            .ToList(),
                    })
                    .ToList(),
            };
        }

        // Scalar annotations only, ordered by key so the document is deterministic. Annotations holding
        // objects or sequences are application-internal and are left out.
        private static List<KVAnnotationSchema> MapAnnotations(KVDefinition definition)
        {
            return definition.Annotations
                .Where(static pair => pair.Value is null || KVSchemaValues.IsScalar(pair.Value))
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new KVAnnotationSchema
                {
                    Key = pair.Key,
                    Value = KVSchemaValues.ToText(pair.Value),
                    Kind = pair.Value is null ? KVSchemaValueKind.Object : KVSchemaValues.KindOf(pair.Value.GetType()),
                })
                .ToList();
        }

        // An item or nested type is addressed by its slot, so its node carries no key of its own.
        private KVNodeSchema MapTypeNode(KVNodeDefinition definition)
        {
            var node = this.MapNode(definition);
            node.Key = string.Empty;
            return node;
        }

        private KVCollectionSchema MapCollection(KVCollectionDefinition collection)
        {
            return new KVCollectionSchema
            {
                Key = collection.SubSegmentPath,
                Label = collection.DisplayName,
                IsReadOnly = collection.IsPatchReadOnly,
                IsInherited = collection.IsInherited,
                NotEmpty = collection.NotEmpty,
                MinCount = collection.MinCount,
                MaxCount = collection.MaxCount,
                AllowedOperations = collection.AllowedOperations?.Order(StringComparer.Ordinal).ToList(),
                Operations = collection.OperationNames.ToList(),
                ItemTypes = collection.ItemDefinitionsByToken.Values
                    .Select(item => this.MapItemType(item.TypeToken, item.NodeDefinition))
                    .ToList(),
                Seeds = collection.Seeds
                    .Select(static seed => new KVSeedSchema { ItemId = seed.ItemId, TypeToken = seed.ItemDefinition.TypeToken })
                    .ToList(),
                AggregateRules = collection.AggregateRules
                    .Select(static rule => new KVAggregateRuleSchema
                    {
                        FieldKey = rule.FieldKey,
                        Comparison = rule.Comparison.ToString(),
                        Threshold = rule.Threshold,
                        ErrorCode = rule.ErrorCode,
                    })
                    .ToList(),
                Annotations = MapAnnotations(collection),
            };
        }

        private KVNestedNodeSchema MapNestedNode(KVNestedNodeDefinition nested)
        {
            return new KVNestedNodeSchema
            {
                Key = nested.SubSegmentPath,
                Label = nested.DisplayName,
                IsReadOnly = nested.IsPatchReadOnly,
                IsInherited = nested.IsInherited,
                IsRequired = nested.IsRequired,
                DefaultType = nested.DefaultTypeToken,
                Types = nested.TypeDefinitionsByToken.Values
                    .Select(type => this.MapItemType(type.TypeToken, type.NodeDefinition))
                    .ToList(),
                Annotations = MapAnnotations(nested),
            };
        }

        // A type already being mapped further up this branch is a recursive definition: the inner occurrence
        // becomes a reference to the outer one, which gets an id, so the schema stays finite.
        private KVItemTypeSchema MapItemType(string token, KVNodeDefinition definition)
        {
            if (this.open.TryGetValue(definition, out var enclosing))
            {
                enclosing.Id ??= this.NewId(enclosing.Token);
                return new KVItemTypeSchema { Token = token, Label = definition.DisplayName, Ref = enclosing.Id };
            }

            var type = new KVItemTypeSchema { Token = token, Label = definition.DisplayName };
            this.open.Add(definition, type);
            try
            {
                type.Node = this.MapTypeNode(definition);
            }
            finally
            {
                this.open.Remove(definition);
            }

            return type;
        }

        // The token, made unique within the document when two recursive types share it.
        private string NewId(string token)
        {
            var id = token;
            var suffix = 1;
            while (!this.ids.Add(id))
            {
                suffix++;
                id = $"{token}_{suffix}";
            }

            return id;
        }
    }
}
