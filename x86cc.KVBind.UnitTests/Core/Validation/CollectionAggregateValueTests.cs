using System.Globalization;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public enum AggregateDeclaration
{
    CollectionOptions,
    ValidationProfile
}

public class CollectionAggregateValueTests : KVModelTestBase
{
    [Theory]
    [InlineData(AggregateDeclaration.CollectionOptions)]
    [InlineData(AggregateDeclaration.ValidationProfile)]
    public void Aggregate_WhenValueIsNotNumeric_ReportsItAtItemFieldAndSkipsThreshold(AggregateDeclaration declaration)
    {
        this.RegisterText(declaration, threshold: 100m);
        var model = this.CreateRoot<AggregateTextModel>();
        var itemId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
        model.Lines.Create(itemId).AmountText = "abc";
        model.Lines.Create().AmountText = "500";
        model.CommitSetup();

        var errors = model.Validate().Errors;

        errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Path = $"Lines/{itemId:D}/AmountText",
            Code = "aggregate_non_numeric",
            Message = $"'Lines/{itemId:D}/AmountText' value 'abc' is not a number and cannot be summed for 'Lines'."
        });
    }

    [Theory]
    [InlineData(AggregateDeclaration.CollectionOptions)]
    [InlineData(AggregateDeclaration.ValidationProfile)]
    public void Aggregate_WhenNumericTextUsesInvariantFormat_SumsItRegardlessOfCurrentCulture(AggregateDeclaration declaration)
    {
        this.RegisterText(declaration, threshold: 100m);
        var model = this.CreateRoot<AggregateTextModel>();
        model.Lines.Create().AmountText = "60.5";
        model.Lines.Create().AmountText = "39.4";
        model.CommitSetup();

        // In de-DE '.' is the group separator, so a culture-sensitive parse would read "60.5" as 605.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            model.Validate().Errors.Should().BeEmpty();

            model.Lines.Create().AmountText = "0.2";
            model.Validate().Errors.Should().ContainSingle().Which.Code.Should().Be("aggregate_less_than");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Aggregate_WhenValueIsBoolean_TreatsItAsNotNumeric()
    {
        this.RegisterModelDefinition<AggregateFlagModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateFlagLine>(item => item.Field(x => x.Flag));
            options.AggregateSum("Flag").LessThan(100m);
        }));
        var model = this.CreateRoot<AggregateFlagModel>();
        model.Lines.Create().Flag = true;
        model.CommitSetup();

        model.Validate().Errors.Should().ContainSingle().Which.Code.Should().Be("aggregate_non_numeric");
    }

    [Theory]
    [InlineData(AggregateDeclaration.CollectionOptions)]
    [InlineData(AggregateDeclaration.ValidationProfile)]
    public void AggregateSelector_WhenCanonicalKeyDiffersFromPropertyName_SumsStoredField(AggregateDeclaration declaration)
    {
        this.RegisterModelDefinition<AggregateKeyedModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateKeyedLine>(item => item.Field(x => x.Amount));
            if (declaration == AggregateDeclaration.CollectionOptions)
            {
                options.AggregateSum<AggregateKeyedLine, decimal>(x => x.Amount).LessThan(100m);
            }
            else
            {
                options.Validation(profiles => profiles.For<KVDefaultValidationProfile>(rules =>
                    rules.AggregateSum<AggregateKeyedLine, decimal>(x => x.Amount).LessThan(100m)));
            }
        }));
        var model = this.CreateRoot<AggregateKeyedModel>();
        model.Lines.Create().Amount = 150m;
        model.CommitSetup();

        model.Validate().Errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Path = "Lines",
            Code = "aggregate_less_than",
            Message = "'Lines' aggregate sum for 'amt' is invalid."
        });
    }

    [Fact]
    public void AggregateSelector_WhenPropertyIsConverted_SumsUnderlyingField()
    {
        this.RegisterModelDefinition<AggregateKeyedModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateKeyedLine>(item => item.Field(x => x.Count));
            options.AggregateSum<AggregateKeyedLine, decimal>(x => x.Count).LessThan(10m);
        }));
        var model = this.CreateRoot<AggregateKeyedModel>();
        model.Lines.Create().Count = 6;
        model.Lines.Create().Count = 5;
        model.CommitSetup();

        model.Validate().Errors.Should().ContainSingle().Which.Code.Should().Be("aggregate_less_than");
    }

    [Fact]
    public void AggregateSelector_WhenNotAProperty_Throws()
    {
        var builder = new KVBindBuilder<AggregateKeyedModel>();

        var act = () => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateKeyedLine>(item => item.Field(x => x.Amount));
            options.AggregateSum<AggregateKeyedLine, decimal>(x => x.Amount + 1m);
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("Aggregate selector must target a property.*")
            .Which.ParamName.Should().Be("selector");
    }

    private void RegisterText(AggregateDeclaration declaration, decimal threshold)
    {
        this.RegisterModelDefinition<AggregateTextModel>(builder => builder.Collection(x => x.Lines, options =>
        {
            options.Item<AggregateTextLine>(item => item.Field(x => x.AmountText));
            if (declaration == AggregateDeclaration.CollectionOptions)
            {
                options.AggregateSum("AmountText").LessThan(threshold);
            }
            else
            {
                options.Validation(profiles => profiles.For<KVDefaultValidationProfile>(rules =>
                    rules.AggregateSum("AmountText").LessThan(threshold)));
            }
        }));
    }
}

public partial class AggregateTextModel : KVRootNode
{
    [KVBind("Lines")]
    public KVCollectionNode<AggregateTextLine> Lines { get; } = new();
}

public partial class AggregateTextLine : KVCollectionItemNode
{
    [KVBind("AmountText")]
    public partial string AmountText { get; set; }
}

public partial class AggregateFlagModel : KVRootNode
{
    [KVBind("Lines")]
    public KVCollectionNode<AggregateFlagLine> Lines { get; } = new();
}

public partial class AggregateFlagLine : KVCollectionItemNode
{
    [KVBind("Flag")]
    public partial bool Flag { get; set; }
}

public partial class AggregateKeyedModel : KVRootNode
{
    [KVBind("Lines")]
    public KVCollectionNode<AggregateKeyedLine> Lines { get; } = new();
}

public partial class AggregateKeyedLine : KVCollectionItemNode
{
    [KVBind("amt")]
    public partial decimal Amount { get; set; }

    [KVBind("cnt")]
    public partial int Count { get; set; }
}
