using AwesomeAssertions;
using Meziantou.Framework.InlineSnapshotTesting;
using System.Text.Json;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class FieldBindingTests : KVModelTestBase
{
    public FieldBindingTests()
    {
        RegisterModelDefinition<FieldTestModel>(modelBuilder =>
        {
            modelBuilder.Field(x => x.BooleanField);
            modelBuilder.Field(x => x.CharField);
            modelBuilder.Field(x => x.IntField);
            modelBuilder.Field(x => x.FloatField);
            modelBuilder.Field(x => x.DoubleField);
            modelBuilder.Field(x => x.DecimalField);
            modelBuilder.Field(x => x.StructField);
            modelBuilder.Field(x => x.StringField);
            modelBuilder.Field(x => x.EnumField, field => field.AllowedValues(FieldEnum.First, FieldEnum.Second, FieldEnum.Third));
            modelBuilder.Field(x => x.DateTimeField);
            modelBuilder.Field(x => x.DateTimeOffsetField);
            modelBuilder.Field(x => x.TimeOnlyField);
            modelBuilder.Field(x => x.DateOnlyField);
            modelBuilder.Field(x => x.TimespanField);
            modelBuilder.Field(x => x.SmartEnumField, field => field.AllowedValues(SmartEnum.All, value => value.Id, value => value.Label));
            modelBuilder.Field(x => x.GuidField);
            modelBuilder.Field(x => x.ArrayOfInts);
            modelBuilder.Field(x => x.ArrayOfStrings);
            modelBuilder.Field(x => x.ArrayOfDates);
            modelBuilder.Field(x => x.ArrayOfEnums);
            modelBuilder.Field(x => x.ArrayOfSmartEnums);
        });
    }

    [Fact]
    public void FieldBinding_WhenRootFieldsAreSet_StoresDraftValuesAtRootPaths()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<FieldTestModel>(model);

        var structValue = new StructTestModel { UnsignedIntField = 7, LongIntField = 77 };
        var dateTimeValue = new DateTime(2026, 4, 15, 11, 30, 45, DateTimeKind.Utc);
        var dateTimeOffsetValue = new DateTimeOffset(2026, 4, 15, 11, 30, 45, TimeSpan.FromHours(2));
        var timeOnlyValue = new TimeOnly(18, 42, 9);
        var dateOnlyValue = new DateOnly(2026, 4, 15);
        var timeSpanValue = TimeSpan.FromMinutes(95);
		var guidValue = Guid.Parse("f38c4671-bb96-481b-9546-6280291e7671");
        int[] arrayOfInts = [1, 2, 3];
        string[] arrayOfStrings = ["1", "2", "3"];
        DateTime[] arrayOfDates = [dateTimeValue.AddDays(1), dateTimeValue.AddMinutes(1), dateTimeValue.AddYears(1)];
        FieldEnum[] arrayOfEnums = [FieldEnum.First, FieldEnum.Third];
        SmartEnum[] arrayOfSmartEnums = [SmartEnum.First, SmartEnum.Third];

        rootNode.BooleanField = true;
        rootNode.CharField = 'A';
        rootNode.IntField = 3;
        rootNode.FloatField = 4.5f;
        rootNode.DoubleField = 5.25d;
        rootNode.DecimalField = 6.125m;
        rootNode.StructField = structValue;
        rootNode.StringField = "notes";
        rootNode.EnumField = FieldEnum.Second;
        rootNode.DateTimeField = dateTimeValue;
        rootNode.DateTimeOffsetField = dateTimeOffsetValue;
        rootNode.TimeOnlyField = timeOnlyValue;
        rootNode.DateOnlyField = dateOnlyValue;
        rootNode.TimespanField = timeSpanValue;
        rootNode.SmartEnumField = SmartEnum.Second;
        rootNode.GuidField = guidValue;
        rootNode.ArrayOfInts = arrayOfInts;
        rootNode.ArrayOfStrings = arrayOfStrings;
        rootNode.ArrayOfDates = arrayOfDates;
        rootNode.ArrayOfEnums = arrayOfEnums;
        rootNode.ArrayOfSmartEnums = arrayOfSmartEnums;
                
        model.Overlay.AddedOrChanged.Should().ContainKey("BooleanField").WhoseValue.Should().Be(true);
        model.Overlay.AddedOrChanged.Should().ContainKey("StringField").WhoseValue.Should().Be("notes");
        model.Overlay.AddedOrChanged.Should().ContainKey("StructField").WhoseValue.Should().Be(structValue);
        model.Overlay.AddedOrChanged.Should().ContainKey("ArrayOfSmartEnums").WhoseValue.Value.Should().BeEquivalentTo(arrayOfSmartEnums);
    }

    [Fact]
    public void FieldBinding_WhenValuesExist_ReadsTypedValues()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<FieldTestModel>(model);
        
        var structValue = new StructTestModel { UnsignedIntField = 9, LongIntField = 99 };
        var dateTimeValue = new DateTime(2027, 1, 1, 7, 15, 0, DateTimeKind.Utc);
        var dateTimeOffsetValue = new DateTimeOffset(2027, 1, 1, 7, 15, 0, TimeSpan.FromHours(-5));
        var timeOnlyValue = new TimeOnly(9, 10, 11);
        var dateOnlyValue = new DateOnly(2027, 1, 2);
        var timeSpanValue = TimeSpan.FromHours(3.5);
        var guidValue = Guid.Parse("c2f27b04-6e65-4c5d-a2db-1b6b0713c689");
        
        int[] arrayOfInts = [1, 2, 3];
        string[] arrayOfStrings = ["1", "2", "3"];
        DateTime[] arrayOfDates = [dateTimeValue.AddDays(1), dateTimeValue.AddMinutes(1), dateTimeValue.AddYears(1)];
        FieldEnum[] arrayOfEnums = [FieldEnum.First, FieldEnum.Third];
        SmartEnum[] arrayOfSmartEnums = [SmartEnum.First, SmartEnum.Third];

        model.Set(nameof(FieldTestModel.BooleanField), true);
        model.Set(nameof(FieldTestModel.CharField), 'Z');
        model.Set(nameof(FieldTestModel.IntField), 30);
        model.Set(nameof(FieldTestModel.FloatField), 40.5f);
        model.Set(nameof(FieldTestModel.DoubleField), 50.25d);
        model.Set(nameof(FieldTestModel.DecimalField), 60.125m);
        model.Set(nameof(FieldTestModel.StructField), structValue);
        model.Set(nameof(FieldTestModel.StringField), "seeded");
        model.Set(nameof(FieldTestModel.EnumField), FieldEnum.Third);
        model.Set(nameof(FieldTestModel.DateTimeField), dateTimeValue);
        model.Set(nameof(FieldTestModel.DateTimeOffsetField), dateTimeOffsetValue);
        model.Set(nameof(FieldTestModel.TimeOnlyField), timeOnlyValue);
        model.Set(nameof(FieldTestModel.DateOnlyField), dateOnlyValue);
        model.Set(nameof(FieldTestModel.TimespanField), timeSpanValue);
        model.Set(nameof(FieldTestModel.SmartEnumField), SmartEnum.Third);
        model.Set(nameof(FieldTestModel.GuidField), guidValue);
        model.Set(nameof(FieldTestModel.ArrayOfInts), arrayOfInts);
        model.Set(nameof(FieldTestModel.ArrayOfStrings), arrayOfStrings);
        model.Set(nameof(FieldTestModel.ArrayOfDates), arrayOfDates);
        model.Set(nameof(FieldTestModel.ArrayOfEnums), arrayOfEnums);
        model.Set(nameof(FieldTestModel.ArrayOfSmartEnums), arrayOfSmartEnums);
        
        InlineSnapshot.Validate(rootNode, """
            BooleanField: true
            CharField: Z
            IntField: 30
            FloatField: 40.5
            DoubleField: 50.25
            DecimalField: 60.125
            StructField:
              UnsignedIntField: 9
              LongIntField: 99
            StringField: seeded
            EnumField: Third
            DateTimeField: 2027-01-01T07:15:00Z
            DateTimeOffsetField: 2027-01-01T07:15:00-05:00
            TimeOnlyField: 09:10:11.0000000
            DateOnlyField: 2027-01-02
            TimespanField: 03:30:00
            SmartEnumField:
              Id: third
              Label: Third
            GuidField: c2f27b04-6e65-4c5d-a2db-1b6b0713c689
            ArrayOfInts:
              - 1
              - 2
              - 3
            ArrayOfStrings:
              - 1
              - 2
              - 3
            ArrayOfDates:
              - 2027-01-02T07:15:00Z
              - 2027-01-01T07:16:00Z
              - 2028-01-01T07:15:00Z
            ArrayOfEnums:
              - First
              - Third
            ArrayOfSmartEnums:
              - Id: first
                Label: First
              - Id: third
                Label: Third
            Id:
            Version:
            """);
    }

    [Fact]
    public void FieldBinding_WhenValueIsMissing_ReadsClrDefaultValue()
    {
        var rootNode = CreateRoot<FieldTestModel>();

        rootNode.BooleanField.Should().BeFalse();
        rootNode.IntField.Should().Be(0);
        rootNode.DecimalField.Should().Be(0m);
        rootNode.GuidField.Should().Be(Guid.Empty);
        rootNode.StringField.Should().BeNull();
    }

    [Fact]
    public void FieldBinding_WhenValueIsStoredAsNull_ReadsClrDefaultValue()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<FieldTestModel>(model);
        model.Set<object?>(nameof(FieldTestModel.DecimalField), null);
        model.Set<string?>(nameof(FieldTestModel.StringField), null);

        rootNode.DecimalField.Should().Be(0m);
        rootNode.StringField.Should().BeNull();
    }

    [Fact]
    public void FieldBinding_WhenSnapshotRoundTripsThroughJson_PreservesDateLookingStringAndDateTime()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<FieldTestModel>(model);
        var dateTimeValue = new DateTime(2027, 1, 1, 7, 15, 0, DateTimeKind.Utc);
        rootNode.StringField = "2027-01-01";
        rootNode.DateTimeField = dateTimeValue;
        CommitSetup(model);

        var restoredSnapshot = JsonSerializer.Deserialize<KVSnapshot>(JsonSerializer.Serialize(model.Snapshot))!;
        var definition = CreateRegistry().Get<FieldTestModel>();
        var restoredModel = new KVModelRoot(KVOverlay.Create(restoredSnapshot, TestUser));
        var restoredRoot = CreateRoot<FieldTestModel>(restoredModel);

        restoredRoot.StringField.Should().Be("2027-01-01");
        restoredRoot.DateTimeField.Should().Be(dateTimeValue);
    }

    [Fact]
    public void FieldBinding_WhenLegacyRawSnapshotJsonIsLoaded_ReadsStringFieldsCorrectly()
    {
        var restoredSnapshot = JsonSerializer.Deserialize<KVSnapshot>(
            """
            {
              "Data": {
                "StringField": "2027-01-01"
              }
            }
            """)!;
        var definition = CreateRegistry().Get<FieldTestModel>();
        var restoredModel = new KVModelRoot(KVOverlay.Create(restoredSnapshot, TestUser));
        var restoredRoot = CreateRoot<FieldTestModel>(restoredModel);

        restoredRoot.StringField.Should().Be("2027-01-01");
    }

    [Fact]
    public void FieldBinding_WhenEnumValueIsOutsideAllowedSet_StoresRawValueForValidation()
    {
        var rootNode = CreateRoot<FieldTestModel>();

        rootNode.EnumField = (FieldEnum)999;
        rootNode.EnumField.Should().Be((FieldEnum)999);
    }

    [Fact]
    public void FieldBinding_WhenSmartEnumTokenIsUnknown_ThrowsOnRead()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<FieldTestModel>(model);
        
        model.Set(nameof(FieldTestModel.SmartEnumField), "unknown");

        var act = () => _ = rootNode.SmartEnumField;

        act.Should().Throw<InvalidOperationException>();
    }
}

public partial class FieldTestModel : KVRootNode
{
    [KVBind(nameof(BooleanField))]
    public partial bool BooleanField { get; set; }

    [KVBind(nameof(CharField))]
    public partial char CharField { get; set; }

    [KVBind(nameof(IntField))]
    public partial int IntField { get; set; }

    [KVBind(nameof(FloatField))]
    public partial float FloatField { get; set; }

    [KVBind(nameof(DoubleField))]
    public partial double DoubleField { get; set; }

    [KVBind(nameof(DecimalField))]
    public partial decimal DecimalField { get; set; }

    [KVBind(nameof(StructField))]
    public partial StructTestModel StructField { get; set; }

    [KVBind(nameof(StringField))]
    public partial string StringField { get; set; } 
    
    [KVBind(nameof(EnumField))]
    public partial FieldEnum EnumField { get; set; }  
    
    [KVBind(nameof(DateTimeField))]
    public partial DateTime DateTimeField { get; set; }
    
    [KVBind(nameof(DateTimeOffsetField))]
    public partial DateTimeOffset DateTimeOffsetField { get; set; }
    
    [KVBind(nameof(TimeOnlyField))]
    public partial TimeOnly TimeOnlyField { get; set; }
    
    [KVBind(nameof(DateOnlyField))]
    public partial DateOnly DateOnlyField { get; set; }
    
    [KVBind(nameof(TimespanField))]
    public partial TimeSpan TimespanField { get; set; }    
    
    [KVBind(nameof(SmartEnumField))]
    public partial SmartEnum SmartEnumField { get; set; }    
    
    [KVBind(nameof(GuidField))]
    public partial Guid GuidField { get; set; }
    
    [KVBind(nameof(ArrayOfInts))]
    public partial int[] ArrayOfInts { get; set; }
    
    [KVBind(nameof(ArrayOfStrings))]
    public partial string[] ArrayOfStrings { get; set; }
    
    [KVBind(nameof(ArrayOfDates))]
    public partial DateTime[] ArrayOfDates { get; set; }
    
    [KVBind(nameof(ArrayOfEnums))]
    public partial FieldEnum[] ArrayOfEnums { get; set; }
    
    [KVBind(nameof(ArrayOfSmartEnums))]
    public partial SmartEnum[] ArrayOfSmartEnums { get; set; }
}

public struct StructTestModel
{
    public uint UnsignedIntField { get; set; }

    public long LongIntField { get; set; }
}

public enum FieldEnum
{
    First,
    Second,
    Third
}

public sealed class SmartEnum : IEquatable<SmartEnum>
{
    public static readonly SmartEnum First = new("first", "First");
    public static readonly SmartEnum Second = new("second", "Second");
    public static readonly SmartEnum Third = new("third", "Third");

    public static IReadOnlyList<SmartEnum> All { get; } =
    [
        First,
        Second,
        Third
    ];

    private SmartEnum(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }

    public string Label { get; }

    public bool Equals(SmartEnum? other)
    {
        return other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is SmartEnum other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Id);
    }

    public override string ToString()
    {
        return Id;
    }
}
