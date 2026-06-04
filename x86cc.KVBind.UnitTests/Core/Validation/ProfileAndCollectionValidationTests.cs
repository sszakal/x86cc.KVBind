using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class ProfileAndCollectionValidationTests : KVModelTestBase
{
    public ProfileAndCollectionValidationTests()
    {
        RegisterModelDefinition<ProfileValidationModel>(builder =>
        {
            builder.Field(x => x.Status);
            builder.Field(x => x.Name, options =>
            {
                options.Validation(profiles => profiles
                    .For<FullValidationProfile>(rules => rules.Required().MaxLength(5)));
            });
        });

        RegisterModelDefinition<CollectionAdvancedValidationModel>(builder =>
        {
            builder.Field(x => x.Mode);
            builder.Collection(x => x.Items, options =>
            {
                options.Item<CollectionAdvancedValidationItem>(item => item.Field(x => x.Amount));
                options.MaxCount(2);
                options.Validation(profiles => profiles.For<FullValidationProfile>(rules =>
                    rules.MinCount(1).AggregateSum<CollectionAdvancedValidationItem, decimal>(x => x.Amount).LessThanOrEqual(100m)));
            });
        });

        RegisterModelDefinition<CollectionBadAggregateValidationModel>(builder =>
        {
            builder.Field(x => x.Mode);
            builder.Collection(x => x.Items, options =>
            {
                options.Item<CollectionBadAggregateValidationItem>(item => item.Field(x => x.AmountText));
                options.Validation(profiles => profiles.For<FullValidationProfile>(rules =>
                    rules.AggregateSum("AmountText").LessThan(100m)));
            });
        });
    }

    [Fact]
    public void ProfileValidation_WhenProfileMatches_EvaluatesFieldProfileRules()
    {
        var model = CreateRoot<ProfileValidationModel>();
        model.Status = ValidationMode.Full;
        model.Name = "toolong";

        model.CommitSetup();
        var validation = model.Validate();

        validation.Errors.Should().Contain(error => error.Path == "Name" && error.Code == "max_length");
    }

    [Fact]
    public void ProfileValidation_WhenProfileDoesNotMatch_SkipsFieldProfileRules()
    {
        var model = CreateRoot<ProfileValidationModel>();
        model.Status = ValidationMode.Quick;
        model.Name = string.Empty;

        model.CommitSetup();
        var validation = model.Validate();

        validation.Errors.Should().NotContain(error => error.Path == "Name");
    }

    [Fact]
    public void CollectionValidation_WhenCollectionRulesRun_ReturnsMaxCountError()
    {
        var model = CreateRoot<CollectionAdvancedValidationModel>();
        model.Mode = ValidationMode.Full;

        var a = model.Items.Create();
        a.Amount = 60m;
        var b = model.Items.Create();
        b.Amount = 50m;
        _ = model.Items.Create();

        model.CommitSetup();
        var validation = model.Validate();

        validation.Errors.Should().Contain(error => error.Path == "Items" && error.Code == "max_count");
    }

    [Fact]
    public void CollectionValidation_WhenAggregateFieldIsNotNumeric_CurrentRuntimeDoesNotThrow()
    {
        var model = CreateRoot<CollectionBadAggregateValidationModel>();
        model.Mode = ValidationMode.Full;
        var item = model.Items.Create();
        item.AmountText = "abc";

        model.CommitSetup();
        var act = () => model.Validate();

        act.Should().NotThrow();
        model.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void PatchValidation_WhenCollectionItemFieldChanges_ReturnsCollectionAggregateError()
    {
        var model = CreateRoot<CollectionAdvancedValidationModel>();
        model.Mode = ValidationMode.Full;
        var item = model.Items.Create();
        item.Amount = 110m;
        var itemId = model.Items.GetItemId(item);

        model.CommitSetup();
        var batch = model.Patch(KVPatchOperation.Set($"/Items/{itemId}/Amount", 120m));

        var validation = batch.Validate();
        validation.Errors.Should().Contain(error => error.Path == "Items" && error.Code == "aggregate_less_than_or_equal");
    }
}

public enum ValidationMode
{
    Quick,
    Full
}

public partial class ProfileValidationModel : KVRootNode
{
    [KVBind("Status")]
    public partial ValidationMode Status { get; set; }

    [KVBind("Name")]
    public partial string Name { get; set; }

    protected override KVValidationProfile GetValidationProfile()
    {
        return Status == ValidationMode.Full ? FullValidationProfile.Instance : QuickValidationProfile.Instance;
    }
}

public partial class CollectionAdvancedValidationModel : KVRootNode
{
    [KVBind("Mode")]
    public partial ValidationMode Mode { get; set; }

    [KVBind("Items")]
    public KVCollectionNode<CollectionAdvancedValidationItem> Items { get; } = new();

    protected override KVValidationProfile GetValidationProfile()
    {
        return Mode == ValidationMode.Full ? FullValidationProfile.Instance : QuickValidationProfile.Instance;
    }
}

public partial class CollectionAdvancedValidationItem : KVCollectionItemNode
{
    [KVBind("Amount")]
    public partial decimal Amount { get; set; }
}

public partial class CollectionBadAggregateValidationModel : KVRootNode
{
    [KVBind("Mode")]
    public partial ValidationMode Mode { get; set; }

    [KVBind("Items")]
    public KVCollectionNode<CollectionBadAggregateValidationItem> Items { get; } = new();

    protected override KVValidationProfile GetValidationProfile()
    {
        return Mode == ValidationMode.Full ? FullValidationProfile.Instance : QuickValidationProfile.Instance;
    }
}

public partial class CollectionBadAggregateValidationItem : KVCollectionItemNode
{
    [KVBind("AmountText")]
    public partial string AmountText { get; set; }
}

public sealed record QuickValidationProfile : KVValidationProfile
{
    public static QuickValidationProfile Instance { get; } = new();

    private QuickValidationProfile()
    {
    }
}

public sealed record FullValidationProfile : KVValidationProfile
{
    public static FullValidationProfile Instance { get; } = new();

    private FullValidationProfile()
    {
    }
}
