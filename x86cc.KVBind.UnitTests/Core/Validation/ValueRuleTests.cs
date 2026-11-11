using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class ValueRuleTests : KVModelTestBase
{
    public ValueRuleTests()
    {
        this.RegisterModelDefinition<VrContract>(builder =>
        {
            builder.Field(x => x.Code, f => f.MinLength(3).MaxLength(6).Pattern("[A-Z]+-?[0-9]*"));
            builder.Field(x => x.Years, f => f.Range(1, 10));
            builder.Field(x => x.Fee, f => f.Min(0m));
            builder.Field(x => x.Ends, f => f.Max(new DateOnly(2030, 12, 31)));
            builder.Field(x => x.Notes, f => f.Validation(profiles => profiles
                .For<VrStrictProfile>(rules => rules.MinLength(5).Pattern("[a-z ]+"))));
            builder.Field(x => x.Share, f => f.Validation(profiles => profiles
                .For<VrStrictProfile>(rules => rules.Range(0m, 100m))));
            builder.Field(x => x.Kind);
            builder.Field(x => x.Reference);
            builder.Field(x => x.Discount);

            builder.NestedNode(x => x.Licensor, party => party
                .Bind<VrParty>("PARTY", p => p.Field(x => x.Name, f => f.Required()))
                .Required());
            builder.NestedNode(x => x.Guarantor, party => party
                .Bind<VrParty>("PARTY", p => p.Field(x => x.Name)));

            builder.Validation((KVGroupRuleBuilder<VrContract> rules) => rules
                .When(a => a.Get(x => x.Kind) == "guaranteed", guaranteed => guaranteed
                    .Required(x => x.Guarantor)
                    .MinLength(x => x.Reference, 4)
                    .Pattern(x => x.Reference, "REF[0-9]+")
                    .Range(x => x.Discount, 0, 50))
                .When(a => a.HasValue(x => x.Guarantor), withGuarantor => withGuarantor
                    .Min(x => x.Discount, 5)));
        });
    }

    [Fact]
    public void ValidValues_ReportNoErrors()
    {
        var contract = this.CreateValid();
        contract.Code = "AB-12";
        contract.Years = 10;
        contract.Fee = 0m;
        contract.Ends = new DateOnly(2030, 12, 31);

        contract.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void UnsetValues_AreNotReportedByValueRules()
    {
        var contract = this.CreateValid();
        contract.Code = string.Empty;

        contract.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void FieldRules_ReportLengthPatternAndBounds()
    {
        var contract = this.CreateValid();
        contract.Code = "ab";
        contract.Years = 11;
        contract.Fee = -0.5m;
        contract.Ends = new DateOnly(2031, 1, 1);

        Describe(contract).Should().Equal(
            "Code:min_length:'Code' must be at least 3 characters.",
            "Code:pattern:'Code' does not match the required format.",
            "Years:max:'Years' must be at most 10.",
            "Fee:min:'Fee' must be at least 0.",
            "Ends:max:'Ends' must be at most 2030-12-31.");
    }

    [Fact]
    public void Pattern_MustMatchTheWholeValue()
    {
        var contract = this.CreateValid();
        contract.Code = "AB-1x";

        Describe(contract).Should().Equal("Code:pattern:'Code' does not match the required format.");
    }

    [Fact]
    public void Bounds_CompareNumbersByValue_WhateverTheStoredType()
    {
        var contract = this.CreateValid();

        // A value written straight to the overlay (a migration, older data) may not have the declared type.
        contract.RootModel().Overlay.Set("Years", new KVValue<long>(0L));

        Describe(contract).Should().Equal("Years:min:'Years' must be at least 1.");
    }

    [Fact]
    public void ProfileRules_ApplyOnlyUnderTheirProfile()
    {
        var contract = this.CreateValid();
        contract.Notes = "Hey";
        contract.Share = 120m;

        contract.Validate().Errors.Should().BeEmpty();

        contract.Strict = true;
        Describe(contract).Should().Equal(
            "Notes:min_length:'Notes' must be at least 5 characters.",
            "Notes:pattern:'Notes' does not match the required format.",
            "Share:max:'Share' must be at most 100.");
    }

    [Fact]
    public void RequiredNestedNode_IsReportedAtTheSlot_UntilASubtypeIsInitialized()
    {
        var contract = this.CreateRoot<VrContract>();

        Describe(contract).Should().Equal("Licensor:required:'Licensor' is required.");

        contract.Patch(KVPatchOperation.Init("/Licensor", "PARTY"));
        Describe(contract).Should().Equal("Licensor/Name:required:'Licensor/Name' is required.");
    }

    [Fact]
    public void GroupRules_CoverNestedSlotsLengthPatternAndBounds_UnderACondition()
    {
        var contract = this.CreateValid();
        contract.Reference = "X1";
        contract.Discount = 60;
        contract.Validate().Errors.Should().BeEmpty();

        contract.Kind = "guaranteed";
        Describe(contract).Should().Equal(
            "Guarantor:required:'Guarantor' is required.",
            "Reference:min_length:'Reference' must be at least 4 characters.",
            "Reference:pattern:'Reference' does not match the required format.",
            "Discount:max:'Discount' must be at most 50.");

        contract.ChooseGuarantor();
        contract.Reference = "REF77";
        contract.Discount = 2;
        Describe(contract).Should().Equal("Discount:min:'Discount' must be at least 5.");
    }

    [Fact]
    public void Definition_ExposesTheDeclaredRules()
    {
        var definition = this.CreateRegistry().Get<VrContract>();

        var code = definition.FindField("Code")!;
        code.MinLength.Should().Be(3);
        code.MaxLength.Should().Be(6);
        code.Pattern.Should().Be("[A-Z]+-?[0-9]*");
        definition.FindField("Years")!.Min.Should().Be(1);
        definition.FindField("Years")!.Max.Should().Be(10);
        definition.FindField("Kind")!.Min.Should().BeNull();
        definition.FindNestedNode("Licensor")!.IsRequired.Should().BeTrue();
        definition.FindNestedNode("Guarantor")!.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void InvalidDeclarations_AreRejectedWhenTheDefinitionIsBuilt()
    {
        var onNumber = () => new KVBindBuilder<VrContract>().Field(x => x.Years, f => f.MinLength(2));
        var badPattern = () => new KVBindBuilder<VrContract>().Field(x => x.Code, f => f.Pattern("[a-"));
        var zeroLength = () => new KVBindBuilder<VrContract>().Field(x => x.Code, f => f.MinLength(0));
        var lengthOrder = () => new KVBindBuilder<VrContract>().Field(x => x.Code, f => f.MaxLength(2).MinLength(3));
        var boundOrder = () => new KVBindBuilder<VrContract>().Field(x => x.Years, f => f.Range(10, 1));

        onNumber.Should().Throw<InvalidOperationException>().WithMessage("MinLength applies to string fields*");
        badPattern.Should().Throw<ArgumentException>();
        zeroLength.Should().Throw<ArgumentOutOfRangeException>();
        lengthOrder.Should().Throw<InvalidOperationException>().WithMessage("MinLength (3) cannot be greater than MaxLength (2).");
        boundOrder.Should().Throw<InvalidOperationException>().WithMessage("Min (10) cannot be greater than Max (1).");
    }

    private static IEnumerable<string> Describe(VrContract contract)
        => contract.Validate().Errors.Select(error => $"{error.Path}:{error.Code}:{error.Message}");

    private VrContract CreateValid()
    {
        var contract = this.CreateRoot<VrContract>();
        contract.ChooseLicensor("Acme");
        return contract;
    }
}

public sealed record VrStrictProfile : KVValidationProfile
{
    private VrStrictProfile()
    {
    }

    public static VrStrictProfile Instance { get; } = new();
}

public partial class VrContract : KVRootNode
{
    public bool Strict { get; set; }

    [KVBind("Code")]
    public partial string? Code { get; set; }

    [KVBind("Years")]
    public partial int? Years { get; set; }

    [KVBind("Fee")]
    public partial decimal? Fee { get; set; }

    [KVBind("Ends")]
    public partial DateOnly? Ends { get; set; }

    [KVBind("Notes")]
    public partial string? Notes { get; set; }

    [KVBind("Share")]
    public partial decimal? Share { get; set; }

    [KVBind("Kind")]
    public partial string? Kind { get; set; }

    [KVBind("Reference")]
    public partial string? Reference { get; set; }

    [KVBind("Discount")]
    public partial int? Discount { get; set; }

    [KVBind("Licensor")]
    public partial VrParty? Licensor { get; private set; }

    [KVBind("Guarantor")]
    public partial VrParty? Guarantor { get; private set; }

    public void ChooseLicensor(string name)
    {
        this.Licensor = new VrParty();
        this.Licensor!.Name = name;
    }

    public void ChooseGuarantor() => this.Guarantor = new VrParty();

    protected override KVValidationProfile GetValidationProfile()
        => this.Strict ? VrStrictProfile.Instance : KVDefaultValidationProfile.Instance;
}

public partial class VrParty : KVNestedNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }
}
