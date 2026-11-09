using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class GroupRuleTests : KVModelTestBase
{
    [Fact]
    public void GroupMaxLength_WhenValueExceedsLimit_ReportsErrorAtGroupFieldPath()
    {
        this.Register(detailsRules: rules => rules.MaxLength(x => x.Name, 5));
        var model = this.CreateRoot<GroupRuleModel>();
        model.Details.Name = "abcdef";
        model.CommitSetup();

        var errors = model.Validate().Errors;

        errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Path = "Details/Name",
            Code = "max_length",
            Message = "'Details/Name' must be at most 5 characters."
        });
    }

    [Theory]
    [InlineData("abcde")]
    [InlineData("")]
    [InlineData(null)]
    public void GroupMaxLength_WhenValueIsWithinLimitOrUnset_ReportsNoError(string? name)
    {
        this.Register(detailsRules: rules => rules.MaxLength(x => x.Name, 5));
        var model = this.CreateRoot<GroupRuleModel>();
        if (name is not null)
        {
            model.Details.Name = name;
        }

        model.CommitSetup();

        model.Validate().Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GroupMaxLength_WhenLimitIsNotPositive_ThrowsWhenDeclared(int maxLength)
    {
        var builder = new KVBindBuilder<GroupRuleModel>();
        builder.Field(x => x.Title);

        var act = () => builder.Validation((KVGroupRuleBuilder<GroupRuleModel> rules) => rules.MaxLength(x => x.Title, maxLength));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("maxLength");
    }

    [Fact]
    public void GroupValidation_WhenDefinitionHasNoMembers_Throws()
    {
        var builder = new KVBindBuilder<GroupRuleModel>();

        var act = () => builder.Validation((KVGroupRuleBuilder<GroupRuleModel> rules) => rules.Required(x => x.Title));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Validation requires at least one field or collection declared in the same definition.");
    }

    [Fact]
    public void GroupRequired_WhenDefinitionHasSeveralMembers_ReportsErrorOnce()
    {
        this.Register(rootRules: rules => rules.Required(x => x.Title));
        var model = this.CreateRoot<GroupRuleModel>();
        model.CommitSetup();

        var errors = model.Validate().Errors;

        errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Path = "Title", Code = "required" });
    }

    [Fact]
    public void GroupCustom_WhenValidatorAddsError_ReportsIt()
    {
        this.Register(rootRules: rules => rules.Custom((accessor, errors) =>
        {
            if (accessor.Get(x => x.Count) > 3)
            {
                errors.Add(new KVValidationError("Count", "too_many", "Count is too high."));
            }
        }));
        var model = this.CreateRoot<GroupRuleModel>();
        model.Count = 4;
        model.CommitSetup();

        var errors = model.Validate().Errors;

        errors.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Path = "Count", Code = "too_many", Message = "Count is too high." });
    }

    [Fact]
    public void GroupCustom_WhenDeclaredOnFieldGroup_ReadsGroupFields()
    {
        string? observed = null;
        this.Register(detailsRules: rules => rules.Custom((accessor, _) => observed = accessor.Get(x => x.Name)));
        var model = this.CreateRoot<GroupRuleModel>();
        model.Details.Name = "inside";
        model.CommitSetup();

        model.Validate();

        observed.Should().Be("inside");
    }

    [Fact]
    public void AccessorGet_WhenFieldIsUnset_ReturnsDefault()
    {
        int? observed = null;
        this.Register(rootRules: rules => rules.Custom((accessor, _) => observed = accessor.Get(x => x.Count)));
        var model = this.CreateRoot<GroupRuleModel>();
        model.CommitSetup();

        model.Validate();

        observed.Should().Be(0);
    }

    [Fact]
    public void AccessorGet_WhenSelectorTypeDoesNotMatchStoredValue_Throws()
    {
        this.Register(rootRules: rules => rules.Custom((accessor, _) => accessor.Get(x => (long)x.Count)));
        var model = this.CreateRoot<GroupRuleModel>();
        model.Count = 5;
        model.CommitSetup();

        var act = () => model.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Field 'Count' contains value type 'System.Int32', which cannot be cast to 'System.Int64'.");
    }

    [Fact]
    public void AccessorGetByKey_ReturnsRawStoredValue()
    {
        object? observed = null;
        this.Register(rootRules: rules => rules.Custom((accessor, _) => observed = accessor.Get("Count")));
        var model = this.CreateRoot<GroupRuleModel>();
        model.Count = 7;
        model.CommitSetup();

        model.Validate();

        observed.Should().Be(7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AccessorGetByKey_WhenKeyIsBlank_Throws(string fieldKey)
    {
        this.Register(rootRules: rules => rules.Custom((accessor, _) => accessor.Get(fieldKey)));
        var model = this.CreateRoot<GroupRuleModel>();
        model.CommitSetup();

        var act = () => model.Validate();

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("fieldKey");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("x", true)]
    public void AccessorHasValue_TreatsWhitespaceStringsAsMissing(string? title, bool expected)
    {
        bool? observed = null;
        this.Register(rootRules: rules => rules.Custom((accessor, _) => observed = accessor.HasValue(x => x.Title)));
        var model = this.CreateRoot<GroupRuleModel>();
        if (title is not null)
        {
            model.Title = title;
        }

        model.CommitSetup();

        model.Validate();

        observed.Should().Be(expected);
    }

    [Fact]
    public void AccessorGetCollectionChildIds_ReturnsActiveIdsInOrder()
    {
        IReadOnlyList<string>? observed = null;
        this.Register(rootRules: rules => rules.Custom((accessor, _) => observed = accessor.GetCollectionChildIds("Lines")));
        var model = this.CreateRoot<GroupRuleModel>();
        var first = model.Lines.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var removed = model.Lines.Create(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        model.Lines.Create(Guid.Parse("00000000-0000-0000-0000-000000000003"));
        model.Lines.RemoveById(model.Lines.GetItemId(removed)).Should().BeTrue();
        model.Lines.MoveById(model.Lines.GetItemId(first), 1).Should().BeTrue();
        model.CommitSetup();

        model.Validate();

        observed.Should().Equal(
            "00000000-0000-0000-0000-000000000003",
            "00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void AccessorGetCollectionChildIds_WhenCollectionIsUnknown_ReturnsEmpty()
    {
        IReadOnlyList<string>? observed = null;
        this.Register(rootRules: rules => rules.Custom((accessor, _) => observed = accessor.GetCollectionChildIds("Missing")));
        var model = this.CreateRoot<GroupRuleModel>();
        model.CommitSetup();

        model.Validate();

        observed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AccessorGetCollectionChildIds_WhenPathIsBlank_Throws(string collectionPath)
    {
        this.Register(rootRules: rules => rules.Custom((accessor, _) => accessor.GetCollectionChildIds(collectionPath)));
        var model = this.CreateRoot<GroupRuleModel>();
        model.CommitSetup();

        var act = () => model.Validate();

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("collectionPath");
    }

    private void Register(
        Action<KVGroupRuleBuilder<GroupRuleModel>>? rootRules = null,
        Action<KVGroupRuleBuilder<GroupRuleDetailsNode>>? detailsRules = null)
    {
        this.RegisterModelDefinition<GroupRuleModel>(builder =>
        {
            builder.Field(x => x.Title);
            builder.Field(x => x.Count);
            builder.FieldGroup(x => x.Details, details =>
            {
                details.Field(x => x.Name);
                if (detailsRules is not null)
                {
                    details.Validation(detailsRules);
                }
            });
            builder.Collection(x => x.Lines, options => options.Item<GroupRuleLine>(item => item.Field(x => x.Amount)));
            if (rootRules is not null)
            {
                builder.Validation(rootRules);
            }
        });
    }
}

public partial class GroupRuleModel : KVRootNode
{
    [KVBind("Title")]
    public partial string Title { get; set; }

    [KVBind("Count")]
    public partial int Count { get; set; }

    [KVBind("Details")]
    public GroupRuleDetailsNode Details { get; } = new();

    [KVBind("Lines")]
    public KVCollectionNode<GroupRuleLine> Lines { get; } = new();
}

public partial class GroupRuleDetailsNode : KVFieldGroupNode
{
    [KVBind("Name")]
    public partial string Name { get; set; }
}

public partial class GroupRuleLine : KVCollectionItemNode
{
    [KVBind("Amount")]
    public partial decimal Amount { get; set; }
}
