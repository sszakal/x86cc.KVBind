using x86cc.KVBind.Core;

namespace x86cc.KVBind.Benchmarks;

// KVBind equivalent of the native object graph in IComponent.cs.
//
//   KvRoot ──┬─ Component (KvComponent, field group)
//            └─ Collection (level 1) ──┬─ … (level 2) ──┬─ … (level 3, leaf)
//
// Every node implements IComponent so the benchmark can read/write the same 16 fields
// through the interface regardless of which graph (native vs KVBind) backs it.
// Field access is routed through the source-generated GetField/SetField accessors,
// which read from / write to the underlying KVOverlay key-value store.

public partial class KvRoot : KVRootNode, IComponent
{
    [KVBind(nameof(BooleanField))] public partial bool BooleanField { get; set; }
    [KVBind(nameof(CharField))] public partial char CharField { get; set; }
    [KVBind(nameof(IntField))] public partial int IntField { get; set; }
    [KVBind(nameof(FloatField))] public partial float FloatField { get; set; }
    [KVBind(nameof(DoubleField))] public partial double DoubleField { get; set; }
    [KVBind(nameof(DecimalField))] public partial decimal DecimalField { get; set; }
    [KVBind(nameof(StringField))] public partial string StringField { get; set; }
    [KVBind(nameof(DateTimeField))] public partial DateTime DateTimeField { get; set; }
    [KVBind(nameof(DateTimeOffsetField))] public partial DateTimeOffset DateTimeOffsetField { get; set; }
    [KVBind(nameof(TimeOnlyField))] public partial TimeOnly TimeOnlyField { get; set; }
    [KVBind(nameof(DateOnlyField))] public partial DateOnly DateOnlyField { get; set; }
    [KVBind(nameof(TimespanField))] public partial TimeSpan TimespanField { get; set; }
    [KVBind(nameof(GuidField))] public partial Guid GuidField { get; set; }
    [KVBind(nameof(ArrayOfInts))] public partial int[] ArrayOfInts { get; set; }
    [KVBind(nameof(ArrayOfStrings))] public partial string[] ArrayOfStrings { get; set; }
    [KVBind(nameof(ArrayOfDates))] public partial DateTime[] ArrayOfDates { get; set; }

    [KVBind(nameof(Component))]
    public KvComponent Component { get; } = new();

    [KVBind(nameof(Collection))]
    public KVCollectionNode<KvLevel1> Collection { get; } = new();

    public IEnumerable<IComponent> GetAllComponents()
        => Collection.SelectMany(x => x.GetAllComponents()).Union([Component]);
}

public partial class KvComponent : KVFieldGroupNode, IComponent
{
    [KVBind(nameof(BooleanField))] public partial bool BooleanField { get; set; }
    [KVBind(nameof(CharField))] public partial char CharField { get; set; }
    [KVBind(nameof(IntField))] public partial int IntField { get; set; }
    [KVBind(nameof(FloatField))] public partial float FloatField { get; set; }
    [KVBind(nameof(DoubleField))] public partial double DoubleField { get; set; }
    [KVBind(nameof(DecimalField))] public partial decimal DecimalField { get; set; }
    [KVBind(nameof(StringField))] public partial string StringField { get; set; }
    [KVBind(nameof(DateTimeField))] public partial DateTime DateTimeField { get; set; }
    [KVBind(nameof(DateTimeOffsetField))] public partial DateTimeOffset DateTimeOffsetField { get; set; }
    [KVBind(nameof(TimeOnlyField))] public partial TimeOnly TimeOnlyField { get; set; }
    [KVBind(nameof(DateOnlyField))] public partial DateOnly DateOnlyField { get; set; }
    [KVBind(nameof(TimespanField))] public partial TimeSpan TimespanField { get; set; }
    [KVBind(nameof(GuidField))] public partial Guid GuidField { get; set; }
    [KVBind(nameof(ArrayOfInts))] public partial int[] ArrayOfInts { get; set; }
    [KVBind(nameof(ArrayOfStrings))] public partial string[] ArrayOfStrings { get; set; }
    [KVBind(nameof(ArrayOfDates))] public partial DateTime[] ArrayOfDates { get; set; }
}

public partial class KvLevel1 : KVCollectionItemNode, IComponent
{
    [KVBind(nameof(BooleanField))] public partial bool BooleanField { get; set; }
    [KVBind(nameof(CharField))] public partial char CharField { get; set; }
    [KVBind(nameof(IntField))] public partial int IntField { get; set; }
    [KVBind(nameof(FloatField))] public partial float FloatField { get; set; }
    [KVBind(nameof(DoubleField))] public partial double DoubleField { get; set; }
    [KVBind(nameof(DecimalField))] public partial decimal DecimalField { get; set; }
    [KVBind(nameof(StringField))] public partial string StringField { get; set; }
    [KVBind(nameof(DateTimeField))] public partial DateTime DateTimeField { get; set; }
    [KVBind(nameof(DateTimeOffsetField))] public partial DateTimeOffset DateTimeOffsetField { get; set; }
    [KVBind(nameof(TimeOnlyField))] public partial TimeOnly TimeOnlyField { get; set; }
    [KVBind(nameof(DateOnlyField))] public partial DateOnly DateOnlyField { get; set; }
    [KVBind(nameof(TimespanField))] public partial TimeSpan TimespanField { get; set; }
    [KVBind(nameof(GuidField))] public partial Guid GuidField { get; set; }
    [KVBind(nameof(ArrayOfInts))] public partial int[] ArrayOfInts { get; set; }
    [KVBind(nameof(ArrayOfStrings))] public partial string[] ArrayOfStrings { get; set; }
    [KVBind(nameof(ArrayOfDates))] public partial DateTime[] ArrayOfDates { get; set; }

    [KVBind(nameof(Collection))]
    public KVCollectionNode<KvLevel2> Collection { get; } = new();

    public IEnumerable<IComponent> GetAllComponents()
        => Collection.SelectMany(x => x.GetAllComponents()).Union([this]);
}

public partial class KvLevel2 : KVCollectionItemNode, IComponent
{
    [KVBind(nameof(BooleanField))] public partial bool BooleanField { get; set; }
    [KVBind(nameof(CharField))] public partial char CharField { get; set; }
    [KVBind(nameof(IntField))] public partial int IntField { get; set; }
    [KVBind(nameof(FloatField))] public partial float FloatField { get; set; }
    [KVBind(nameof(DoubleField))] public partial double DoubleField { get; set; }
    [KVBind(nameof(DecimalField))] public partial decimal DecimalField { get; set; }
    [KVBind(nameof(StringField))] public partial string StringField { get; set; }
    [KVBind(nameof(DateTimeField))] public partial DateTime DateTimeField { get; set; }
    [KVBind(nameof(DateTimeOffsetField))] public partial DateTimeOffset DateTimeOffsetField { get; set; }
    [KVBind(nameof(TimeOnlyField))] public partial TimeOnly TimeOnlyField { get; set; }
    [KVBind(nameof(DateOnlyField))] public partial DateOnly DateOnlyField { get; set; }
    [KVBind(nameof(TimespanField))] public partial TimeSpan TimespanField { get; set; }
    [KVBind(nameof(GuidField))] public partial Guid GuidField { get; set; }
    [KVBind(nameof(ArrayOfInts))] public partial int[] ArrayOfInts { get; set; }
    [KVBind(nameof(ArrayOfStrings))] public partial string[] ArrayOfStrings { get; set; }
    [KVBind(nameof(ArrayOfDates))] public partial DateTime[] ArrayOfDates { get; set; }

    [KVBind(nameof(Collection))]
    public KVCollectionNode<KvLevel3> Collection { get; } = new();

    public IEnumerable<IComponent> GetAllComponents() => Collection;
}

public partial class KvLevel3 : KVCollectionItemNode, IComponent
{
    [KVBind(nameof(BooleanField))] public partial bool BooleanField { get; set; }
    [KVBind(nameof(CharField))] public partial char CharField { get; set; }
    [KVBind(nameof(IntField))] public partial int IntField { get; set; }
    [KVBind(nameof(FloatField))] public partial float FloatField { get; set; }
    [KVBind(nameof(DoubleField))] public partial double DoubleField { get; set; }
    [KVBind(nameof(DecimalField))] public partial decimal DecimalField { get; set; }
    [KVBind(nameof(StringField))] public partial string StringField { get; set; }
    [KVBind(nameof(DateTimeField))] public partial DateTime DateTimeField { get; set; }
    [KVBind(nameof(DateTimeOffsetField))] public partial DateTimeOffset DateTimeOffsetField { get; set; }
    [KVBind(nameof(TimeOnlyField))] public partial TimeOnly TimeOnlyField { get; set; }
    [KVBind(nameof(DateOnlyField))] public partial DateOnly DateOnlyField { get; set; }
    [KVBind(nameof(TimespanField))] public partial TimeSpan TimespanField { get; set; }
    [KVBind(nameof(GuidField))] public partial Guid GuidField { get; set; }
    [KVBind(nameof(ArrayOfInts))] public partial int[] ArrayOfInts { get; set; }
    [KVBind(nameof(ArrayOfStrings))] public partial string[] ArrayOfStrings { get; set; }
    [KVBind(nameof(ArrayOfDates))] public partial DateTime[] ArrayOfDates { get; set; }
}
