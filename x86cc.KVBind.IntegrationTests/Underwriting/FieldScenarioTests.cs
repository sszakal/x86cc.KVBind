using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Sample.Api.Underwriting;

namespace x86cc.KVBind.IntegrationTests.Underwriting;

// 1a scalar types and defaults · 1b allowed values · 1c value rules
public sealed class FieldScenarioTests : UnderwritingTestBase
{
    // ── 1a ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void NewApplication_HasItsDeclaredDefaults()
    {
        var application = CreateNew();

        application.Status.Should().Be("draft");
        application.LineOfBusiness.Should().Be(LineOfBusiness.Property);
        application.Basics.IsRenewal.Should().BeFalse();
        application.Basics.ReviewSla.Should().Be(TimeSpan.FromDays(5));
        application.Basics.Channel.Should().Be(SalesChannel.Broker);
        application.Basics.Coverages.Should().Be(CoverageOptions.Fire | CoverageOptions.Theft);
        application.Premises.Construction.Should().Be("masonry");
        application.Premises.Floors.Should().Be(1);
        application.Insured.Should().BeOfType<InsuredCompany>("COMPANY is the default subtype");
    }

    [Fact]
    public void BindingStoredData_DoesNotApplyDefaults()
    {
        var application = CreateNew();
        application.Basics.Channel = SalesChannel.Online;
        application.Patch(KVPatchOperation.Unset("/Basics/ReviewSla"));

        var reloaded = CommitAndReload(application);

        reloaded.Basics.Channel.Should().Be(SalesChannel.Online);
        reloaded.Basics.ReviewSla.Should().BeNull("defaults apply when an application is born, not when it is bound");
    }

    [Fact]
    public void Set_ConvertsJsonToEveryScalarKind()
    {
        var application = CreateNew();
        var broker = Guid.NewGuid();

        application.Patch(
            Set("/Basics/YearsInBusiness", "12"),
            Set("/Basics/AnnualRevenue", "\"2500000.50\""),
            Set("/Basics/IsRenewal", "true"),
            Set("/Basics/EffectiveDate", "\"2027-03-01\""),
            Set("/Basics/ReceivedAt", "\"2026-11-14T09:30:00Z\""),
            Set("/Basics/PreferredCallTime", "\"14:30:00\""),
            Set("/Basics/ReviewSla", "\"3.00:00:00\""),
            Set("/Basics/BrokerReference", $"\"{broker}\""),
            Set("/Basics/Channel", "\"Online\""),
            Set("/Basics/Coverages", "\"Fire, Flood, Liability\""),
            Set("/LineOfBusiness", "\"Cyber\""));

        var basics = CommitAndReload(application).Basics;
        basics.YearsInBusiness.Should().Be(12);
        basics.AnnualRevenue.Should().Be(2_500_000.50m);
        basics.IsRenewal.Should().BeTrue();
        basics.EffectiveDate.Should().Be(new DateOnly(2027, 3, 1));
        basics.ReceivedAt.Should().Be(new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero));
        basics.PreferredCallTime.Should().Be(new TimeOnly(14, 30));
        basics.ReviewSla.Should().Be(TimeSpan.FromDays(3));
        basics.BrokerReference.Should().Be(broker);
        basics.Channel.Should().Be(SalesChannel.Online);
        basics.Coverages.Should().Be(CoverageOptions.Fire | CoverageOptions.Flood | CoverageOptions.Liability);
    }

    [Theory]
    [InlineData("/Basics/YearsInBusiness", "1.5")] // lossy: 1.5 is not an int
    [InlineData("/Basics/YearsInBusiness", "\"many\"")] // text is not a number
    [InlineData("/Basics/IsRenewal", "\"maybe\"")]
    [InlineData("/Basics/EffectiveDate", "\"31/02/2027\"")]
    [InlineData("/Basics/Channel", "\"Telepathy\"")] // not a member
    [InlineData("/Basics/Channel", "7")] // number that is not a member
    [InlineData("/Basics/Coverages", "64")] // flag bits that are not declared
    [InlineData("/Basics/ApplicantName", "42")] // a number is not a string
    public void Set_RejectsValuesThatDoNotFitTheFieldType(string path, string json)
    {
        var application = CreateNew();

        var act = () => application.Patch(Set(path, json));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Unset_ClearsANullableField_AndNullDoesTheSame()
    {
        var application = CreateNew();
        application.Patch(Set("/Basics/YearsInBusiness", "3"), Set("/Basics/AnnualRevenue", "10"));

        application.Patch(KVPatchOperation.Unset("/Basics/YearsInBusiness"), Set("/Basics/AnnualRevenue", "null"));

        application.Basics.YearsInBusiness.Should().BeNull();
        application.Basics.AnnualRevenue.Should().BeNull();
    }

    // ── 1b ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllowedValues_AcceptDeclaredTokens_IncludingNumbersSentAsJson()
    {
        var application = CreateNew();

        application.Patch(
            Set("/Classification/Industry", "\"retail\""),
            Set("/Classification/RiskTier", "\"B\""),
            Set("/Classification/Territory", "3"),
            Set("/Classification/DeductibleOption", "\"split\""),
            Set("/Classification/Exclusions", "[\"war\",\"cyber\"]"),
            Set("/Tags", "[\"priority\"]"));

        Errors(application).Should().BeEmpty();
        application.Classification.Territory.Should().Be(3);
        application.Classification.Exclusions.Should().Equal("war", "cyber");
    }

    [Fact]
    public void UnknownTokens_AreKept_AndReportedByValidation()
    {
        var application = CreateNew();

        application.Patch(
            Set("/Classification/Industry", "\"mining\""),
            Set("/Classification/Exclusions", "[\"war\",\"aliens\"]"));

        Errors(application).Should().Contain(("Classification/Industry", "allowed_values"));
        Errors(application).Should().Contain(error => error.Path.StartsWith("Classification/Exclusions") && error.Code == "allowed_values");
        var reloaded = CommitIgnoringValidation(application);
        reloaded.Model.Get<object?>("Classification/Industry").Should().Be("mining", "an unknown token is kept so it can be corrected later");
    }

    [Fact]
    public void UnknownNonTokenValues_AreRejected_WhenSet()
    {
        var application = CreateNew();

        var act = () => application.Patch(Set("/Classification/Territory", "9"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*allowed values*");
    }

    [Fact]
    public void AllowedValueComponents_KeepTheirTemplatesAndPlaceholders()
    {
        var field = Definition.FindNode("Classification")!.FindField("DeductibleOption")!;

        var split = field.AllowedValueOptions.Single(option => option.Id == "split");
        split.Template.Should().Be("Split — {Property:C} property, {Liability:C} liability");
        split.Placeholders.Select(placeholder => placeholder.Name).Should().Equal("Property", "Liability");
    }

    // ── 1c ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/Basics/ApplicationNumber", "\"1001\"", "Basics/ApplicationNumber", "pattern")]
    [InlineData("/Basics/YearsInBusiness", "201", "Basics/YearsInBusiness", "max")]
    [InlineData("/Basics/AnnualRevenue", "-1", "Basics/AnnualRevenue", "min")]
    [InlineData("/Contact/Email", "\"not-an-email\"", "Contact/Email", "pattern")]
    [InlineData("/Contact/Website", "\"a.b\"", "Contact/Website", "min_length")]
    [InlineData("/Contact/Employees", "0", "Contact/Employees", "min")]
    [InlineData("/Contact/DiscountPercent", "25.01", "Contact/DiscountPercent", "max")]
    [InlineData("/Contact/InceptionDate", "\"2031-01-01\"", "Contact/InceptionDate", "max")]
    [InlineData("/Premises/Floors", "0", "Premises/Floors", "min")]
    public void ValueRules_ReportTheirCodeAtTheFieldPath(string path, string json, string errorPath, string code)
    {
        var application = CreateNew();

        application.Patch(Set(path, json));

        Errors(application).Should().ContainSingle().Which.Should().Be((errorPath, code));
    }

    [Fact]
    public void ValueRules_SkipUnsetValues_AndBoundariesAreInclusive()
    {
        var application = CreateNew();

        application.Patch(
            Set("/Contact/Email", "\"\""),
            Set("/Contact/Employees", "1"),
            Set("/Contact/DiscountPercent", "25"),
            Set("/Contact/InceptionDate", "\"2030-12-31\""));

        Errors(application).Should().BeEmpty();
    }

    [Fact]
    public void RequiredField_ReportsRequired_WhenCleared()
    {
        var application = CreateNew();

        application.Patch(KVPatchOperation.Unset("/Basics/ApplicationNumber"));

        Errors(application).Should().ContainSingle().Which.Should().Be(("Basics/ApplicationNumber", "required"));
    }

    private static UnderwritingApplication CommitIgnoringValidation(UnderwritingApplication application)
    {
        var snapshot = application.RootModel().Overlay.Snapshot.Clone();
        snapshot.Apply(application.RootModel().Overlay.ToCommit(DateTimeOffset.UtcNow));
        return Bind(snapshot);
    }
}
