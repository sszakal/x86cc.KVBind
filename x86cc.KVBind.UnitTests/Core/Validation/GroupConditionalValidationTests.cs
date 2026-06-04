using System;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class GroupConditionalValidationTests : KVModelTestBase
{
    public GroupConditionalValidationTests()
    {
        RegisterModelDefinition<GroupValidationRootModel>(builder =>
        {
            builder.Field(x => x.Title);
            builder.FieldGroup(x => x.Details, details =>
            {
                details.Field(x => x.Mode);
                details.Field(x => x.Country);
                details.Field(x => x.TaxId);
                details.Validation(profiles => 
                    profiles.For<FullValidationProfile>(rules =>
                    rules.When(
                        accessor => string.Equals(accessor.Get(x => x.Country), "US", StringComparison.Ordinal),
                        when => when.Required(x => x.TaxId))));
            });
        });
    }

    [Fact]
    public void PatchValidation_WhenGroupConditionMatches_AppliesNestedProfileRule()
    {
        var root = CreateRoot<GroupValidationRootModel>();
        root.Details.Mode = ValidationMode.Full;

        root.CommitSetup();
        var batch = root.Patch(
            KVPatchOperation.Set("/Details/Country", "US"),
            KVPatchOperation.Set("/Details/TaxId", ""));

        var validation = batch.Validate();

        validation.Errors.Should().Contain(error => error.Path == "Details/TaxId" && error.Code == "required");
    }

    [Fact]
    public void PatchValidation_WhenGroupConditionDoesNotMatch_DoesNotAddConditionalErrors()
    {
        var root = CreateRoot<GroupValidationRootModel>();
        root.Details.Mode = ValidationMode.Full;

        root.CommitSetup();
        var batch = root.Patch(
            KVPatchOperation.Set("/Details/Country", "FR"),
            KVPatchOperation.Set("/Details/TaxId", ""));

        var validation = batch.Validate();

        validation.Errors.Should().NotContain(error => error.Path == "Details/TaxId" && error.Code == "required");
    }

    [Fact]
    public void CommitValidation_WhenProfileDoesNotMatch_DoesNotAddConditionalErrors()
    {
        var root = CreateRoot<GroupValidationRootModel>();
        root.Details.Mode = ValidationMode.Quick;
        root.Details.Country = "US";
        root.Details.TaxId = "";

        root.CommitSetup();
        var validation = root.Validate();

        validation.Errors.Should().NotContain(error => error.Path == "Details/TaxId" && error.Code == "required");
    }

    [Fact]
    public void PatchValidation_WhenScopeOutsideGroup_RunsFullValidation()
    {
        var root = CreateRoot<GroupValidationRootModel>();
        root.Details.Mode = ValidationMode.Full;
        root.Details.Country = "US";
        root.Details.TaxId = "";

        root.CommitSetup();
        var batch = root.Patch(KVPatchOperation.Set("/Title", "new"));

        var validation = batch.Validate();

        validation.Errors.Should().Contain(error => error.Path == "Details/TaxId" && error.Code == "required");
    }
}

public partial class GroupValidationRootModel : KVRootNode
{
    [KVBind("Title")]
    public partial string Title { get; set; }

    [KVBind("Details")]
    public GroupValidationDetailsNode Details { get; } = new();

    protected override KVValidationProfile GetValidationProfile()
    {
        return Details.Mode == ValidationMode.Full ? FullValidationProfile.Instance : QuickValidationProfile.Instance;
    }
}

public partial class GroupValidationDetailsNode : KVFieldGroupNode
{
    [KVBind("Mode")]
    public partial ValidationMode Mode { get; set; }

    [KVBind("Country")]
    public partial string Country { get; set; }

    [KVBind("TaxId")]
    public partial string TaxId { get; set; }
}
