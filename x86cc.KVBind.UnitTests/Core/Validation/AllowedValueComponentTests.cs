using AwesomeAssertions;
using Meziantou.Framework.InlineSnapshotTesting;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class AllowedValueComponentTests : KVModelTestBase
{
    public AllowedValueComponentTests()
    {
        RegisterModelDefinition<CompensationModel>(builder =>
        {
            builder.Field(x => x.CompensationType, options =>
            {
                options.AllowedValue(CompensationEmployeeType.Manager, "manager_flat", "Manager (Flat Fee)");
                options.AllowedValueComponent(CompensationEmployeeType.Assistant, "assistant_hourly", "Assistant (Hourly)", component => component
                    .Template("Assistant {Hours}h @ {Rate}")
                    .Placeholder<int>("Hours")
                    .Placeholder<decimal>("Rate"));
            });
        });
    }

    [Fact]
    public void AllowedValueComponent_WhenSelectedByIdToken_PersistsToken()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<CompensationModel>(model);
        
        rootNode.CommitSetup();
        rootNode.Patch(KVPatchOperation.Set("/CompensationType", "assistant_hourly"));

        model.Snapshot.Data.Should().NotContainKey("CompensationType");
        
        rootNode.GetAllChanges().Changes.Should().Contain(change => change.Path == "CompensationType");
        rootNode.CompensationType.Should().Be(CompensationEmployeeType.Assistant);
        
        InlineSnapshot.Validate(model.Overlay.AddedOrChanged, """
                                                CompensationType: Assistant
                                                """);
        
        var validation = rootNode.Validate();
        
        InlineSnapshot.Validate(validation, """
                                            Errors: []
                                            Scope: []
                                            IsFullEvaluation: true
                                            """);
    }

    [Fact]
    public void AllowedValueComponent_WhenTokenIsUnknown_ValidationReturnsAllowedValuesError()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<CompensationModel>(model);
        rootNode.CommitSetup();

        rootNode.Patch(KVPatchOperation.Set("/CompensationType", "unknown_token"));

        var validation = rootNode.Validate();
        
        InlineSnapshot.Validate(validation, """
                                            Errors:
                                              - Path: CompensationType
                                                Code: allowed_values
                                                Message: Value for field 'CompensationType' is not part of configured allowed values.
                                            Scope: []
                                            IsFullEvaluation: true
                                            """);
    }

    [Fact]
    public void AllowedValueComponent_WhenArgPathPatched_ThrowsAsUnsupportedFieldPath()
    {
        var rootNode = CreateRoot<CompensationModel>();
        rootNode.CommitSetup();
        
        rootNode.Patch(KVPatchOperation.Set("/CompensationType", "assistant_hourly"));

        var act = () => rootNode.Patch(KVPatchOperation.Set("/CompensationType/$args/Hours", "bad"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AllowedValueComponent_WhenSelectedByEnumValue_ValidationPasses()
    {
        var rootNode = CreateRoot<CompensationModel>();
        rootNode.CompensationType = CompensationEmployeeType.Assistant;

        rootNode.CommitSetup();
        var validation = rootNode.Validate();

        validation.Errors.Should().NotContain(error => error.Path == "CompensationType" && error.Code == "allowed_values");
    }
}

public enum CompensationEmployeeType
{
    Manager,
    Assistant
}

public partial class CompensationModel : KVRootNode
{
    [KVBind("CompensationType")]
    public partial CompensationEmployeeType CompensationType { get; set; }
}
