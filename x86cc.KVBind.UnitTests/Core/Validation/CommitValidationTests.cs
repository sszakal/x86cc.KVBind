using AwesomeAssertions;
using Meziantou.Framework.InlineSnapshotTesting;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class CommitValidationTests : KVModelTestBase
{
    public CommitValidationTests()
    {
        RegisterModelDefinition<ValidationChangeSetModel>(builder =>
        {
            builder.Field(x => x.Name, options => options.Required());
            builder.FieldGroup(x => x.General, group => group.Field(x => x.Code, options => options.Required()));
            builder.Collection(x => x.Items, options =>
            {
                options.NotEmpty();
                options.Item<ValidationChangeSetItemNode>(item => item.Field(x => x.Name, field => field.Required()));
            });
        });
    }

    [Fact]
    public void PatchValidation_WhenFieldChanges_ReturnsActiveErrorsForPatchScope()
    {
        var root = CreateRoot<ValidationChangeSetModel>();
        root.Name = "Valid";
        root.General.Code = "G1";
        root.Items.Create().Name = "item";

        root.CommitSetup();
        var batch = root.Patch(KVPatchOperation.Set("/Name", ""));

        var validation = batch.Validate();

        validation.IsFullEvaluation.Should().BeTrue();
        
        InlineSnapshot.Validate(validation, """
            Errors:
              - Path: Name
                Code: required
                Message: 'Name' is required.
            Scope: []
            IsFullEvaluation: true
            """);
    }

    [Fact]
    public void CommitValidation_WhenFullValidationRuns_ReturnsAllActiveErrors()
    {
        var root = CreateRoot<ValidationChangeSetModel>();

        root.CommitSetup();
        root.Name = string.Empty;
        root.General.Code = string.Empty;

        var validation = root.Validate();

        validation.IsFullEvaluation.Should().BeTrue();
        
        InlineSnapshot.Validate(validation, """
            Errors:
              - Path: Name
                Code: required
                Message: 'Name' is required.
              - Path: Items
                Code: not_empty
                Message: 'Items' collection cannot be empty.
              - Path: Items
                Code: min_count
                Message: 'Items' collection must contain at least 1 item(s).
              - Path: Items
                Code: not_empty
                Message: 'Items' collection cannot be empty.
              - Path: Items
                Code: min_count
                Message: 'Items' collection must contain at least 1 item(s).
              - Path: General/Code
                Code: required
                Message: 'General/Code' is required.
            Scope: []
            IsFullEvaluation: true
            """);
    }

    [Fact]
    public void CommitValidation_WhenValidationErrorsExist_Throws()
    {
        var root = CreateRoot<ValidationChangeSetModel>();
        root.Name = "Valid";
        root.General.Code = "G1";
        root.Items.Create().Name = "item";

        root.CommitSetup();
        root.Name = string.Empty;

        var apply = root.CommitOverlay;

        apply.Should().Throw<KVChangeSetValidationException>();
    }

    [Fact]
    public void PatchValidation_WhenCollectionChanges_EvaluatesCollectionRules()
    {
        var root = CreateRoot<ValidationChangeSetModel>();
        root.Name = "Valid";
        root.General.Code = "G1";
        var item = root.Items.Create();
        item.Name = "item";
        var itemId = root.Items.GetItemId(item);

        root.CommitSetup();
        var batch = root.Patch(KVPatchOperation.Remove($"/Items/{itemId}"));

        var validation = batch.Validate();
        
        InlineSnapshot.Validate(validation, """
            Errors:
              - Path: Items
                Code: not_empty
                Message: 'Items' collection cannot be empty.
              - Path: Items
                Code: min_count
                Message: 'Items' collection must contain at least 1 item(s).
              - Path: Items
                Code: not_empty
                Message: 'Items' collection cannot be empty.
              - Path: Items
                Code: min_count
                Message: 'Items' collection must contain at least 1 item(s).
            Scope: []
            IsFullEvaluation: true
            """);
    }
}

public partial class ValidationChangeSetModel : KVRootNode
{
    [KVBind("Name")]
    public partial string Name { get; set; }

    [KVBind("General")]
    public ValidationChangeSetGeneralNode General { get; set; } = new();

    [KVBind("Items")]
    public KVCollectionNode<ValidationChangeSetItemNode> Items { get; set; } = new();
}

public partial class ValidationChangeSetGeneralNode : KVFieldGroupNode
{
    [KVBind("Code")]
    public partial string Code { get; set; }
}

public partial class ValidationChangeSetItemNode : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string Name { get; set; }
}
