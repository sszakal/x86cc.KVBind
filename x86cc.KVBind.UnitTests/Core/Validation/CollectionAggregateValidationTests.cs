using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public enum AggregateComparison
{
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

public class CollectionAggregateValidationTests : KVModelTestBase
{
    [Theory]
    [InlineData(AggregateComparison.LessThan, 99, null)]
    [InlineData(AggregateComparison.LessThan, 100, "aggregate_less_than")]
    [InlineData(AggregateComparison.LessThanOrEqual, 100, null)]
    [InlineData(AggregateComparison.LessThanOrEqual, 101, "aggregate_less_than_or_equal")]
    [InlineData(AggregateComparison.GreaterThan, 101, null)]
    [InlineData(AggregateComparison.GreaterThan, 100, "aggregate_greater_than")]
    [InlineData(AggregateComparison.GreaterThanOrEqual, 100, null)]
    [InlineData(AggregateComparison.GreaterThanOrEqual, 99, "aggregate_greater_than_or_equal")]
    public void CollectionAggregate_WhenDeclaredOnCollectionOptions_EvaluatesComparisonAtThreshold(AggregateComparison comparison, int total, string? expectedCode)
    {
        this.RegisterModelDefinition<AggregateValidationModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateValidationLine>(item => item.Field(x => x.Amount));
            var aggregate = options.AggregateSum<AggregateValidationLine, decimal>(x => x.Amount);
            _ = comparison switch
            {
                AggregateComparison.LessThan => aggregate.LessThan(100m),
                AggregateComparison.LessThanOrEqual => aggregate.LessThanOrEqual(100m),
                AggregateComparison.GreaterThan => aggregate.GreaterThan(100m),
                _ => aggregate.GreaterThanOrEqual(100m)
            };
        }));
        var model = this.CreateRoot<AggregateValidationModel>();
        model.Lines.Create().Amount = total - 40;
        model.Lines.Create().Amount = 40m;
        model.CommitSetup();

        var errors = model.Validate().Errors;

        if (expectedCode is null)
        {
            errors.Should().BeEmpty();
        }
        else
        {
            errors.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new { Path = "Lines", Code = expectedCode });
        }
    }

    [Theory]
    [InlineData(AggregateComparison.GreaterThan, 100, "aggregate_greater_than")]
    [InlineData(AggregateComparison.GreaterThanOrEqual, 99, "aggregate_greater_than_or_equal")]
    public void CollectionAggregate_WhenDeclaredInValidationProfile_EvaluatesGreaterThanComparisons(AggregateComparison comparison, int total, string expectedCode)
    {
        this.RegisterModelDefinition<AggregateValidationModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateValidationLine>(item => item.Field(x => x.Amount));
            options.Validation(profiles => profiles.For<KVValidationProfile>(rules =>
            {
                var aggregate = rules.AggregateSum("Amount");
                _ = comparison == AggregateComparison.GreaterThan ? aggregate.GreaterThan(100m) : aggregate.GreaterThanOrEqual(100m);
            }));
        }));
        var model = this.CreateRoot<AggregateValidationModel>();
        model.Lines.Create().Amount = total;
        model.CommitSetup();

        model.Validate().Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Path = "Lines", Code = expectedCode });
    }

    [Fact]
    public void CollectionAggregate_WhenDeclaredByFieldKey_SumsOnlyActiveItemsAndSkipsUnsetValues()
    {
        this.RegisterModelDefinition<AggregateValidationModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateValidationLine>(item => item.Field(x => x.Amount));
            options.AggregateSum("Amount").LessThanOrEqual(100m);
        }));
        var model = this.CreateRoot<AggregateValidationModel>();
        model.Lines.Create().Amount = 60m;
        var removed = model.Lines.Create();
        removed.Amount = 60m;
        _ = model.Lines.Create();
        model.CommitSetup();

        model.Validate().Errors.Should().ContainSingle(error => error.Code == "aggregate_less_than_or_equal");

        model.Lines.RemoveById(model.Lines.GetItemId(removed));

        model.Validate().Errors.Should().BeEmpty();
    }

    [Fact]
    public void CollectionAggregate_WhenCollectionIsNested_ReportsErrorAtCanonicalCollectionPath()
    {
        this.RegisterModelDefinition<AggregateValidationModel>(builder =>
        {
            builder.Collection(x => x.Lines, options => options.Item<AggregateValidationLine>(item => item.Field(x => x.Amount)));
            builder.Collection(x => x.Groups, options => options.Item<AggregateValidationGroup>(group => group.Collection(x => x.Lines, lines =>
            {
                lines.Item<AggregateValidationLine>(item => item.Field(x => x.Amount));
                lines.AggregateSum<AggregateValidationLine, decimal>(x => x.Amount).LessThan(10m);
            })));
        });
        var model = this.CreateRoot<AggregateValidationModel>();
        var group = model.Groups.Create();
        group.Lines.Create().Amount = 25m;
        var groupId = model.Groups.GetItemId(group);
        model.CommitSetup();

        model.Validate().Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Path = $"Groups/{groupId}/Lines", Code = "aggregate_less_than" });
    }

    [Fact]
    public void CollectionCountRules_WhenDeclaredOnCollectionOptions_ReportEachViolationOnce()
    {
        this.RegisterModelDefinition<AggregateValidationModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateValidationLine>(item => item.Field(x => x.Amount));
            options.NotEmpty();
            options.MinCount(2);
        }));
        var model = this.CreateRoot<AggregateValidationModel>();
        model.CommitSetup();

        model.Validate().Errors.Select(error => error.Code).Should().BeEquivalentTo("not_empty", "min_count");
    }

    [Fact]
    public void CollectionAggregate_WhenSelectorIsNotAProperty_Throws()
    {
        var act = () => this.RegisterAndBuild(options => options.AggregateSum<AggregateValidationLine, decimal>(x => x.Amount + 1m));

        act.Should().Throw<ArgumentException>()
            .WithMessage("Aggregate selector must target a property.*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CollectionAggregate_WhenFieldKeyIsBlank_Throws(string fieldKey)
    {
        var act = () => this.RegisterAndBuild(options => options.AggregateSum(fieldKey));

        act.Should().Throw<ArgumentException>()
            .WithMessage("Aggregate field key is required.*");
    }

    private void RegisterAndBuild(Action<KVCollectionOptionsBuilder<AggregateValidationModel, AggregateValidationLine>> configure)
    {
        this.RegisterModelDefinition<AggregateValidationModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateValidationLine>(item => item.Field(x => x.Amount));
            configure(options);
        }));
        _ = this.CreateRegistry().Get<AggregateValidationModel>();
    }
}

public partial class AggregateValidationModel : KVRootNode
{
    [KVBind("Lines")]
    public KVCollectionNode<AggregateValidationLine> Lines { get; } = new();

    [KVBind("Groups")]
    public KVCollectionNode<AggregateValidationGroup> Groups { get; } = new();
}

public partial class AggregateValidationLine : KVCollectionItemNode
{
    [KVBind("Amount")]
    public partial decimal Amount { get; set; }
}

public partial class AggregateValidationGroup : KVCollectionItemNode
{
    [KVBind("Lines")]
    public KVCollectionNode<AggregateValidationLine> Lines { get; } = new();
}
