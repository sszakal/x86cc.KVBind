using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class CollectionRuleBuilderTests : KVModelTestBase
{
    public CollectionRuleBuilderTests()
    {
        this.RegisterModelDefinition<RuleRootNode>(builder =>
            builder.Collection(x => x.Items, options => options.Item<RuleItemNode>(item => item.Field(x => x.Amount))));
    }

    [Fact]
    public void BuildGlobalRules_WhenNoRulesConfigured_ReturnsEmpty()
    {
        var builder = new KVCollectionRuleBuilder<RuleItemNode>();

        builder.BuildGlobalRules().Should().BeEmpty();
    }

    [Fact]
    public void BuildGlobalRules_WhenAggregateRuleOnly_ReturnsSingleRule()
    {
        var builder = new KVCollectionRuleBuilder<RuleItemNode>();
        builder.AggregateSum("Amount").LessThan(10m);

        builder.BuildGlobalRules().Should().ContainSingle();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void BuildGlobalRules_WhenCountRuleConfigured_ReturnsRuleMatchingAnyProfile(bool notEmpty, bool minCount, bool maxCount)
    {
        var builder = new KVCollectionRuleBuilder<RuleItemNode>();
        if (notEmpty)
        {
            builder.NotEmpty();
        }

        if (minCount)
        {
            builder.MinCount(1);
        }

        if (maxCount)
        {
            builder.MaxCount(1);
        }

        var rule = builder.BuildGlobalRules().Should().ContainSingle().Subject;

        rule.ProfileMatches(KVDefaultValidationProfile.Instance).Should().BeTrue();
    }

    [Fact]
    public void BuildGlobalRules_WhenEvaluated_ReportsCountViolations()
    {
        var root = this.CreateRoot<RuleRootNode>();
        var builder = new KVCollectionRuleBuilder<RuleItemNode>();
        builder.MinCount(1).MaxCount(1);
        var rule = builder.BuildGlobalRules().Single();

        var errors = new List<KVValidationError>();
        rule.Evaluate(root, "Items", string.Empty, errors);
        errors.Should().ContainSingle().Which.Code.Should().Be("min_count");

        root.Items.Create();
        root.Items.Create();
        errors.Clear();
        rule.Evaluate(root, "Items", string.Empty, errors);
        errors.Should().ContainSingle().Which.Code.Should().Be("max_count");

        root.Items.RemoveById(root.Items.GetItemId(root.Items.First()));
        errors.Clear();
        rule.Evaluate(root, "Items", string.Empty, errors);
        errors.Should().BeEmpty();
    }

    private sealed class RuleRootNode : KVRootNode
    {
        public RuleCollectionNode Items { get; } = new();
    }

    private sealed class RuleCollectionNode : KVCollectionNode<RuleItemNode>;

    private sealed class RuleItemNode : KVCollectionItemNode
    {
        public decimal? Amount
        {
            get => this.GetField<decimal?>("Amount");
            set => this.SetField("Amount", value);
        }
    }
}
