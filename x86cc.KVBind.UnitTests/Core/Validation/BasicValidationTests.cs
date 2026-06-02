using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class BasicValidationTests : KVModelTestBase
{
    public BasicValidationTests()
    {
        RegisterModelDefinition<ValidationRootNode>(builder =>
        {
            builder.Field(x => x.Title, options => options.Required());
            builder.Collection(x => x.Items, options =>
            {
                options.NotEmpty();
                options.Item<ValidationItemNode>(item => item.Field(x => x.Amount));
            });
        });
    }

    [Fact]
    public void Validation_WhenRequiredFieldsAndCollectionsAreEmpty_ReturnsErrors()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ValidationRootNode>(model);
        
        var result = root.Validate();

        result.Errors.Should().Contain(error => error.Path == "Title" && error.Code == "required");
        result.Errors.Should().Contain(error => error.Path == "Items" && error.Code == "not_empty");
    }

    [Fact]
    public void Validation_WhenCollectionMeetsRequirements_ReturnsNoNotEmptyError()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ValidationRootNode>(model);

        root.Title = "Valid";
        var item = root.Items.Create();
        item.Amount = 10;

        var result = root.Validate();

        result.Errors.Should().NotContain(error => error.Path == "Items" && error.Code == "not_empty");
    }

    private sealed class ValidationRootNode : KVRootNode
    {
        public ValidationCollectionNode Items { get; } = new();

        public string? Title
        {
            get => GetField<string?>("Title");
            set => SetField("Title", value);
        }
    }

    private sealed class ValidationCollectionNode : KVCollectionNode<ValidationItemNode>;

    private sealed class ValidationItemNode : KVCollectionItemNode
    {
        public int Amount
        {
            get => GetField<int>("Amount");
            set => SetField("Amount", value);
        }
    }
}
