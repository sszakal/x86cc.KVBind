using System.Text.Json;
using System.Xml.Serialization;
using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Schema;

namespace x86cc.KVBind.UnitTests.Schema;

public enum SchemaLevel
{
    Low = 1,
    Medium = 2,
    High = 3,
}

public class KVSchemaMapperTests
{
    private static readonly KVSchemaDocument Schema = KVSchemaMapper.Map(BuildDefinition(), nameof(SchemaPolicy));

    [Fact]
    public void Document_CarriesModelNameSchemaVersionAndUnkeyedRoot()
    {
        Schema.Model.Should().Be("SchemaPolicy");
        Schema.SchemaVersion.Should().Be(2);
        Schema.Root.Key.Should().BeEmpty();
        Schema.Root.Operations.Should().Equal("SUBMIT");
    }

    [Theory]
    [InlineData("Title", KVSchemaValueKind.String, false, true)]
    [InlineData("Count", KVSchemaValueKind.Integer, false, false)]
    [InlineData("Amount", KVSchemaValueKind.Decimal, false, true)]
    [InlineData("Ratio", KVSchemaValueKind.Number, false, false)]
    [InlineData("Active", KVSchemaValueKind.Boolean, false, false)]
    [InlineData("Starts", KVSchemaValueKind.Date, false, true)]
    [InlineData("Stamp", KVSchemaValueKind.DateTime, false, false)]
    [InlineData("Level", KVSchemaValueKind.Enum, false, false)]
    [InlineData("Tags", KVSchemaValueKind.String, true, true)]
    public void Fields_MapValueKindArrayAndNullability(string key, KVSchemaValueKind kind, bool isArray, bool isNullable)
    {
        var field = Field(Schema.Root, key);

        field.Kind.Should().Be(kind);
        field.IsArray.Should().Be(isArray);
        field.IsNullable.Should().Be(isNullable);
    }

    [Fact]
    public void Field_CarriesLabelConstraintsAndFlags()
    {
        var title = Field(Schema.Root, "Title");
        title.Label.Should().Be("Policy title");
        title.IsRequired.Should().BeTrue();
        title.MinLength.Should().Be(3);
        title.MaxLength.Should().Be(80);
        title.Pattern.Should().Be("[A-Z].*");

        var amount = Field(Schema.Root, "Amount");
        amount.Min.Should().Be("0");
        amount.Max.Should().Be("1000.5");
        amount.IsReadOnly.Should().BeTrue();

        Field(Schema.Root, "Starts").Min.Should().Be("2020-01-01");
    }

    [Fact]
    public void Field_DefaultsAreInvariantText()
    {
        var count = Field(Schema.Root, "Count");
        count.HasDefault.Should().BeTrue();
        count.DefaultValue.Should().Be("3");

        Field(Schema.Root, "Level").DefaultValue.Should().Be("Medium");
        Field(Schema.Root, "Ratio").HasDefault.Should().BeFalse();
    }

    [Fact]
    public void EnumField_ListsItsMembers()
    {
        var level = Field(Schema.Root, "Level");

        level.EnumMembers.Should().Equal("Low", "Medium", "High");
        level.IsFlags.Should().BeFalse();
    }

    [Fact]
    public void ScalarAllowedValues_KeepDeclarationOrderLabelsAndComponents()
    {
        var coverage = Field(Schema.Root, "Coverage").AllowedValues!;

        coverage.AppliesToElements.Should().BeFalse();
        coverage.Options.Select(o => o.Id).Should().Equal("basic", "collision");
        coverage.Options[0].Label.Should().Be("Basic");

        var collision = coverage.Options[1];
        collision.Template.Should().Be("Collision — {Deductible:C}");
        collision.Placeholders.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Name = "Deductible",
            Label = "Deductible amount",
            Kind = KVSchemaValueKind.Decimal,
            IsRequired = true,
        });
    }

    [Fact]
    public void ElementAllowedValues_ApplyToElements()
    {
        var tags = Field(Schema.Root, "Tags").AllowedValues!;

        tags.AppliesToElements.Should().BeTrue();
        tags.Options.Select(o => (o.Id, o.Label)).Should().Equal(("urgent", "Urgent"), ("vip", "VIP"));
    }

    [Fact]
    public void TokenAllowedValues_UseTheTokenAsIdAndTheSelectedLabel()
    {
        var level = Field(Schema.Root, "Level").AllowedValues!;

        level.Options.Select(o => (o.Id, o.Label)).Should().Equal(("1", "Low"), ("2", "Medium"), ("3", "High"));
    }

    [Fact]
    public void FieldWithoutAllowedValues_HasNone()
    {
        Field(Schema.Root, "Title").AllowedValues.Should().BeNull();
    }

    [Fact]
    public void Annotations_KeepScalarsSortedAndDropObjects()
    {
        var annotations = Field(Schema.Root, "Title").Annotations;

        annotations.Select(a => a.Key).Should().Equal("ui:control", "ui:order");
        annotations[0].Should().BeEquivalentTo(new { Value = "text", Kind = KVSchemaValueKind.String });
        annotations[1].Should().BeEquivalentTo(new { Value = "1", Kind = KVSchemaValueKind.Integer });
    }

    [Fact]
    public void Group_IsNestedWithItsFlags()
    {
        var group = Schema.Root.Groups.Should().ContainSingle().Subject;

        group.Key.Should().Be("Holder");
        group.Label.Should().Be("Policy holder");
        group.IsResettable.Should().BeTrue();
        group.Fields.Select(f => f.Key).Should().Equal("Name");
        group.Annotations.Should().ContainSingle().Which.Value.Should().Be("section");
    }

    [Fact]
    public void Collection_CarriesItemTypesRulesSeedsAndOperations()
    {
        var items = Schema.Root.Collections.Should().ContainSingle().Subject;

        items.Key.Should().Be("Items");
        items.Label.Should().Be("Covered items");
        items.MinCount.Should().Be(1);
        items.MaxCount.Should().Be(10);
        items.AllowedOperations.Should().Equal("ADD", "REMOVE", "RENUMBER");
        items.Operations.Should().Equal("RENUMBER");
        items.Seeds.Should().ContainSingle().Which.Should().BeEquivalentTo(new { ItemId = "base", TypeToken = "ITEM" });
        items.AggregateRules.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            FieldKey = "Value",
            Comparison = "LessThanOrEqual",
            Threshold = 5000m,
        });

        items.ItemTypes.Select(t => t.Token).Should().Equal("ITEM", "VEHICLE");
        var vehicle = items.ItemTypes[1].Node;
        vehicle.Key.Should().BeEmpty();
        vehicle.Fields.Select(f => f.Key).Should().Equal("Value", "Plate");
        vehicle.Operations.Should().Equal("INSPECT");
    }

    [Fact]
    public void Collection_WithoutRestriction_AllowsEveryOperation()
    {
        var items = Schema.Root.Collections[0].ItemTypes[0].Node;

        items.Collections.Should().BeEmpty();
        KVSchemaMapper.Map(BuildDefinition(restrictItems: false), "x").Root.Collections[0].AllowedOperations.Should().BeNull();
    }

    [Fact]
    public void NestedNode_CarriesTypesDefaultAndRequired()
    {
        var holder = Schema.Root.NestedNodes.Should().ContainSingle().Subject;

        holder.Key.Should().Be("Payer");
        holder.IsRequired.Should().BeTrue();
        holder.DefaultType.Should().Be("PERSON");
        holder.Types.Select(t => t.Token).Should().Equal("PERSON", "COMPANY");
        holder.Types[1].Node.Fields.Should().ContainSingle().Which.Key.Should().Be("CompanyName");
    }

    [Fact]
    public void Mapping_IsDeterministic()
    {
        var again = KVSchemaMapper.Map(BuildDefinition(), nameof(SchemaPolicy));

        JsonSerializer.Serialize(again).Should().Be(JsonSerializer.Serialize(Schema));
    }

    [Fact]
    public void Document_RoundTripsThroughJsonWithDefaultOptions()
    {
        var json = JsonSerializer.Serialize(Schema);

        var back = JsonSerializer.Deserialize<KVSchemaDocument>(json);

        JsonSerializer.Serialize(back).Should().Be(json);
    }

    [Fact]
    public void Document_RoundTripsThroughXml()
    {
        var serializer = new XmlSerializer(typeof(KVSchemaDocument));
        using var writer = new StringWriter();
        serializer.Serialize(writer, Schema);

        using var reader = new StringReader(writer.ToString());
        var back = (KVSchemaDocument)serializer.Deserialize(reader)!;

        JsonSerializer.Serialize(back).Should().Be(JsonSerializer.Serialize(Schema));
    }

    private static KVFieldSchema Field(KVNodeSchema node, string key) => node.Fields.Single(f => f.Key == key);

    private static KVNodeDefinition BuildDefinition(bool restrictItems = true)
    {
        var builder = new KVBindBuilder<SchemaPolicy>();
        builder.Field(x => x.Title, f => f
            .DisplayName("Policy title")
            .Required()
            .MinLength(3)
            .MaxLength(80)
            .Pattern("[A-Z].*")
            .Annotate("ui:order", 1)
            .Annotate("ui:control", "text")
            .Annotate("internal:callback", new object()));
        builder.Field(x => x.Count, f => f.Default(3));
        builder.Field(x => x.Amount, f => f.Range(0m, 1000.5m).PatchReadOnly());
        builder.Field(x => x.Ratio);
        builder.Field(x => x.Active);
        builder.Field(x => x.Starts, f => f.Min(new DateOnly(2020, 1, 1)));
        builder.Field(x => x.Stamp);
        builder.Field(x => x.Level, f => f
            .Default(SchemaLevel.Medium)
            .AllowedValues([SchemaLevel.Low, SchemaLevel.Medium, SchemaLevel.High], level => (int)level, level => level.ToString()));
        builder.Field(x => x.Coverage, f => f
            .AllowedValue("basic", "basic", "Basic")
            .AllowedValueComponent("collision", "collision", "Collision", c => c
                .Template("Collision — {Deductible:C}")
                .Placeholder<decimal>("Deductible", "Deductible amount")));
        builder.Field(x => x.Tags, f => f
            .AllowedElementValue("urgent", "urgent", "Urgent")
            .AllowedElementValue("vip", "vip", "VIP"));

        builder.FieldGroup(
            x => x.Holder,
            holder => holder.Field(x => x.Name),
            options => options.DisplayName("Policy holder").Resettable().Annotate("ui:role", "section"));

        builder.Collection(x => x.Items, items =>
        {
            items.DisplayName("Covered items");
            items.Item<SchemaItem>("ITEM", item => item.Field(x => x.Value));
            items.Item<SchemaVehicle>("VEHICLE", vehicle =>
            {
                vehicle.Field(x => x.Value);
                vehicle.Field(x => x.Plate);
                vehicle.Operation(x => x.Inspect);
            });
            items.Seed<SchemaItem>("base");
            items.MinCount(1).MaxCount(10);
            items.AggregateSum<SchemaItem, decimal>(x => x.Value).LessThanOrEqual(5000m);
            items.Operation<int>("RENUMBER", x => x.Renumber);
            if (restrictItems)
            {
                items.AllowOperations(KVPatchOperations.Remove, KVPatchOperations.Add, "RENUMBER");
            }
        });

        builder.NestedNode(x => x.Payer, payer => payer
            .Bind<SchemaPerson>("PERSON", p => p.Field(x => x.FullName))
            .Bind<SchemaCompany>("COMPANY", c => c.Field(x => x.CompanyName))
            .DefaultType<SchemaPerson>()
            .Required());

        builder.Operation(x => x.Submit);
        builder.Migration(2, m => m.Backfill(KVTarget.Root.Seg("Count"), _ => 3));
        return builder.Build();
    }
}

public partial class SchemaPolicy : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Count")]
    public partial int Count { get; set; }

    [KVBind("Amount")]
    public partial decimal? Amount { get; set; }

    [KVBind("Ratio")]
    public partial double Ratio { get; set; }

    [KVBind("Active")]
    public partial bool Active { get; set; }

    [KVBind("Starts")]
    public partial DateOnly? Starts { get; set; }

    [KVBind("Stamp")]
    public partial DateTimeOffset Stamp { get; set; }

    [KVBind("Level")]
    public partial SchemaLevel Level { get; set; }

    [KVBind("Coverage")]
    public partial string? Coverage { get; set; }

    [KVBind("Tags")]
    public partial string[]? Tags { get; set; }

    [KVBind("Holder")]
    public SchemaHolder Holder { get; } = new();

    [KVBind("Items")]
    public KVCollectionNode<SchemaItem> Items { get; } = new();

    [KVBind("Payer")]
    public partial SchemaPayer? Payer { get; private set; }

    public void Submit()
    {
        // Only declared: the tests need the operation's signature, not its effect.
    }

    public void Renumber(int start)
    {
        // Only declared: the tests need the operation's signature, not its effect.
    }
}

public partial class SchemaHolder : KVFieldGroupNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }
}

public partial class SchemaItem : KVCollectionItemNode
{
    [KVBind("Value")]
    public partial decimal Value { get; set; }
}

public partial class SchemaVehicle : SchemaItem
{
    [KVBind("Plate")]
    public partial string? Plate { get; set; }

    public void Inspect()
    {
        // Only declared: the tests need the operation's signature, not its effect.
    }
}

public abstract partial class SchemaPayer : KVNestedNode;

public partial class SchemaPerson : SchemaPayer
{
    [KVBind("FullName")]
    public partial string? FullName { get; set; }
}

public partial class SchemaCompany : SchemaPayer
{
    [KVBind("CompanyName")]
    public partial string? CompanyName { get; set; }
}
