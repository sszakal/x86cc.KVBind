using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class AllowedElementValuesTests : KVModelTestBase
{
    public AllowedElementValuesTests()
    {
        this.RegisterModelDefinition<ElementValuesModel>(builder =>
        {
            builder.Field(x => x.StatusArray, field => field.AllowedElementValues(SmartEnum.All, value => value.Id, value => value.Label));
            builder.Field(x => x.StatusList, field => field.AllowedElementValues(SmartEnum.All, value => value.Id, value => value.Label));
            builder.Field(x => x.NumberedEnums, field => field.AllowedElementValues([FieldEnum.First, FieldEnum.Second], value => (int)value));
            builder.Field(x => x.CompensationTypes, field =>
            {
                field.AllowedElementValue(CompensationEmployeeType.Manager, "manager_flat", "Manager (Flat Fee)");
                field.AllowedElementValueComponent(CompensationEmployeeType.Assistant, "assistant_hourly", "Assistant (Hourly)", component => component
                    .Template("Assistant {Hours}h")
                    .Placeholder<int>("Hours"));
                field.AllowedElementValue(CompensationEmployeeType.Assistant, "manager_flat", "Duplicate id is ignored");
            });
        });
    }

    [Fact]
    public void AllowedElementValues_WhenArrayIsSet_StoresTokensAndReadsBackArray()
    {
        var model = new KVModelRoot();
        var root = this.CreateRoot<ElementValuesModel>(model);

        root.StatusArray = [SmartEnum.Third, SmartEnum.First];

        model.Overlay.Changes["StatusArray"].Value.Should().BeEquivalentTo(new[] { "third", "first" }, options => options.WithStrictOrdering());
        root.StatusArray.Should().BeOfType<SmartEnum[]>()
            .Which.Should().Equal(SmartEnum.Third, SmartEnum.First);
    }

    [Fact]
    public void AllowedElementValues_WhenListIsSet_ReadsBackList()
    {
        var root = this.CreateRoot<ElementValuesModel>();

        root.StatusList = [SmartEnum.Second];

        root.StatusList.Should().BeOfType<List<SmartEnum>>()
            .Which.Should().Equal(SmartEnum.Second);
    }

    [Fact]
    public void AllowedElementValues_WhenPatchedWithTokenArray_DenormalizesElements()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.CommitSetup();

        root.Patch(KVPatchOperation.Set("/StatusArray", new[] { "first", "second" }));

        root.StatusArray.Should().Equal(SmartEnum.First, SmartEnum.Second);
        root.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void AllowedElementValues_WhenPatchedWithJsonArrayString_DenormalizesElements()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.CommitSetup();

        root.Patch(KVPatchOperation.Set("/StatusList", """["third","first"]"""));

        root.StatusList.Should().Equal(SmartEnum.Third, SmartEnum.First);
        root.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void AllowedElementValues_WhenPatchedWithJsonElement_DenormalizesElements()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.CommitSetup();
        using var document = JsonDocument.Parse("""["second"]""");

        root.Patch(KVPatchOperation.Set("/StatusArray", document.RootElement.Clone()));

        root.StatusArray.Should().Equal(SmartEnum.Second);
    }

    [Fact]
    public void AllowedElementValues_WhenJsonArrayContainsUnknownToken_KeepsDraftAndReportsAllowedValues()
    {
        var model = new KVModelRoot();
        var root = this.CreateRoot<ElementValuesModel>(model);
        root.CommitSetup();

        root.Patch(KVPatchOperation.Set("/StatusArray", """["first","bogus"]"""));

        model.Overlay.Changes["StatusArray"].Value.Should().Be("""["first","bogus"]""");
        root.Validate().Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Path = "StatusArray", Code = "allowed_values" });
    }

    [Fact]
    public void AllowedElementValues_WhenTokensNeedNumericConversion_ConvertsToConfiguredTokenType()
    {
        var model = new KVModelRoot();
        var root = this.CreateRoot<ElementValuesModel>(model);
        root.CommitSetup();
        using var document = JsonDocument.Parse("[1, 0]");

        // JSON numbers are read as long; the configured token type is int.
        root.Patch(KVPatchOperation.Set("/NumberedEnums", document.RootElement.Clone()));

        model.Overlay.Changes["NumberedEnums"].Value.Should().BeEquivalentTo(new[] { 1, 0 }, options => options.WithStrictOrdering());
        root.NumberedEnums.Should().Equal(FieldEnum.Second, FieldEnum.First);
    }

    [Fact]
    public void AllowedElementValues_WhenSetterReceivesDisallowedElement_Throws()
    {
        var root = this.CreateRoot<ElementValuesModel>();

        var act = () => root.NumberedEnums = [FieldEnum.First, FieldEnum.Third];

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not part of allowed values*");
    }

    [Fact]
    public void AllowedElementValues_WhenSetToNull_ClearsField()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.StatusArray = [SmartEnum.First];
        root.CommitSetup();

        root.StatusArray = null;

        root.StatusArray.Should().BeNull();
        root.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == "StatusArray");
        root.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void AllowedElementValues_WhenPatchedWithNull_ClearsField()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.CompensationTypes = [CompensationEmployeeType.Manager];
        root.CommitSetup();

        root.Patch(KVPatchOperation.Set("/CompensationTypes", null));

        root.CompensationTypes.Should().BeNull();
    }

    [Fact]
    public void AllowedElementValues_WhenSnapshotRoundTripsThroughJson_ReadsBackElements()
    {
        var model = new KVModelRoot();
        var root = this.CreateRoot<ElementValuesModel>(model);
        root.StatusArray = [SmartEnum.Second, SmartEnum.Third];
        root.StatusList = [SmartEnum.First];
        root.NumberedEnums = [FieldEnum.Second];
        root.CompensationTypes = [CompensationEmployeeType.Assistant];
        this.CommitSetup(model);

        var restoredSnapshot = JsonSerializer.Deserialize<KVSnapshot>(JsonSerializer.Serialize(model.Snapshot))!;
        var restored = this.CreateRoot<ElementValuesModel>(new KVModelRoot(KVOverlay.Create(restoredSnapshot, TestUser)));

        restored.StatusArray.Should().Equal(SmartEnum.Second, SmartEnum.Third);
        restored.StatusList.Should().Equal(SmartEnum.First);
        restored.NumberedEnums.Should().Equal(FieldEnum.Second);
        restored.CompensationTypes.Should().Equal(CompensationEmployeeType.Assistant);
        restored.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void AllowedElementValue_WhenExplicitValuesAreSet_StoresIdsAndReadsBackElements()
    {
        var model = new KVModelRoot();
        var root = this.CreateRoot<ElementValuesModel>(model);

        root.CompensationTypes = [CompensationEmployeeType.Assistant, CompensationEmployeeType.Manager];

        model.Overlay.Changes["CompensationTypes"].Value.Should().BeEquivalentTo(new[] { "assistant_hourly", "manager_flat" }, options => options.WithStrictOrdering());
        root.CompensationTypes.Should().Equal(CompensationEmployeeType.Assistant, CompensationEmployeeType.Manager);
    }

    [Fact]
    public void AllowedElementValue_WhenPatchedWithIds_DenormalizesElements()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.CommitSetup();

        root.Patch(KVPatchOperation.Set("/CompensationTypes", new[] { "manager_flat", "assistant_hourly" }));

        root.CompensationTypes.Should().Equal(CompensationEmployeeType.Manager, CompensationEmployeeType.Assistant);
        root.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void AllowedElementValue_WhenIdIsRegisteredTwice_FirstRegistrationWins()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.CommitSetup();

        root.Patch(KVPatchOperation.Set("/CompensationTypes", new[] { "manager_flat" }));

        root.CompensationTypes.Should().Equal(CompensationEmployeeType.Manager);
    }

    [Fact]
    public void AllowedElementValue_WhenIdIsUnknown_ReportsAllowedValues()
    {
        var root = this.CreateRoot<ElementValuesModel>();
        root.CommitSetup();

        root.Patch(KVPatchOperation.Set("/CompensationTypes", """["manager_flat","unknown"]"""));

        root.Validate().Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Path = "CompensationTypes", Code = "allowed_values" });
    }
}

public partial class ElementValuesModel : KVRootNode
{
    [KVBind("StatusArray")]
    public partial SmartEnum[]? StatusArray { get; set; }

    [KVBind("StatusList")]
    public partial List<SmartEnum>? StatusList { get; set; }

    [KVBind("NumberedEnums")]
    public partial FieldEnum[]? NumberedEnums { get; set; }

    [KVBind("CompensationTypes")]
    public partial CompensationEmployeeType[]? CompensationTypes { get; set; }
}
