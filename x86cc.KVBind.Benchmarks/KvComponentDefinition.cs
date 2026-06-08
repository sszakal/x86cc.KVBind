using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.Benchmarks;

// Builds the runtime KVBind schema for the KvRoot graph. Model attributes alone do not
// declare fields/collections — the schema is defined here with KVBindBuilder<T>.
public static class KvComponentDefinition
{
    private static KVNodeDefinition? _cached;

    public static KVNodeDefinition Build() => _cached ??= BuildCore();

    private static KVNodeDefinition BuildCore()
    {
        var builder = new KVBindBuilder<KvRoot>();
        AddComponentFields(builder);

        builder.FieldGroup(x => x.Component, AddComponentFields);

        builder.Collection(x => x.Collection, level1 => level1.Item<KvLevel1>(l1 =>
        {
            AddComponentFields(l1);
            l1.Collection(x => x.Collection, level2 => level2.Item<KvLevel2>(l2 =>
            {
                AddComponentFields(l2);
                l2.Collection(x => x.Collection, level3 => level3.Item<KvLevel3>(AddComponentFields));
            }));
        }));

        return builder.Build();
    }

    // Declares all 16 IComponent fields on any KVBind node builder. The selector targets the
    // IComponent member, so the canonical key resolves to the property name on every node type.
    private static void AddComponentFields<T>(KVBindBuilder<T> builder)
        where T : KVNode, IComponent
    {
        builder.Field(x => x.BooleanField);
        builder.Field(x => x.CharField);
        builder.Field(x => x.IntField);
        builder.Field(x => x.FloatField);
        builder.Field(x => x.DoubleField);
        builder.Field(x => x.DecimalField);
        builder.Field(x => x.StringField);
        builder.Field(x => x.DateTimeField);
        builder.Field(x => x.DateTimeOffsetField);
        builder.Field(x => x.TimeOnlyField);
        builder.Field(x => x.DateOnlyField);
        builder.Field(x => x.TimespanField);
        builder.Field(x => x.GuidField);
        builder.Field(x => x.ArrayOfInts);
        builder.Field(x => x.ArrayOfStrings);
        builder.Field(x => x.ArrayOfDates);
    }
}
