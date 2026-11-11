using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class StructuralChangeReactionTests : KVModelTestBase
{
    public StructuralChangeReactionTests()
    {
        this.RegisterModelDefinition<ScrRoot>(builder =>
        {
            builder.Field(x => x.Title);

            builder.NestedNode(x => x.Party, party =>
            {
                party.Bind<ScrPerson>("PERSON", person => person.Field(x => x.Name, f => f.Default("anonymous")));
                party.Bind<ScrCompany>("COMPANY", company => company.Field(x => x.CompanyName));
            });

            builder.Collection(x => x.Items, items => items.Item<ScrItem>(item =>
            {
                item.Field(x => x.Text);
                item.NestedNode(x => x.Detail, detail => detail.Bind<ScrPerson>("PERSON", person => person.Field(x => x.Name)));
            }));

            builder.OnChange(path => path.Field(x => x.Title), x => x.Track);
            builder.OnChange(path => path.NestedNode(x => x.Party).Any(), x => x.Track);
            builder.OnChange(path => path.Collection(x => x.Items).Any(), x => x.Track);
            builder.OnChange(path => path.Collection(x => x.Items).NestedNode(x => x.Detail).Any(), x => x.Track);
        });

        this.RegisterModelDefinition<ScrDefaultedRoot>(builder =>
        {
            builder.NestedNode(x => x.Party, party => party
                .Bind<ScrPerson>("PERSON", person => person.Field(x => x.Name, f => f.Default("anonymous")))
                .DefaultType("PERSON"));
            builder.OnChange(path => path.NestedNode(x => x.Party).Any(), x => x.Track);
        });
    }

    [Fact]
    public void InitPatch_NotifiesTheSlot_AfterDefaultsAreApplied()
    {
        var root = this.CreateRoot<ScrRoot>();

        root.Patch(KVPatchOperation.Init("/Party", "PERSON"));

        root.Log.Should().Equal("Party:Added:<null>->ScrPerson(anonymous)");
    }

    [Fact]
    public void InitPatch_OverAnotherType_ReportsTheReplacedTypeToken()
    {
        var root = this.CreateRoot<ScrRoot>();
        root.Patch(KVPatchOperation.Init("/Party", "PERSON"));
        root.Log.Clear();

        root.Patch(KVPatchOperation.Init("/Party", "COMPANY"));

        root.Log.Should().Equal("Party:Added:PERSON->ScrCompany");
    }

    [Fact]
    public void DropPatch_NotifiesTheSlot_OnlyWhenItWasInitialized()
    {
        var root = this.CreateRoot<ScrRoot>();

        root.Patch(KVPatchOperation.Drop("/Party"));
        root.Log.Should().BeEmpty();

        root.Patch(KVPatchOperation.Init("/Party", "COMPANY"));
        root.Log.Clear();
        root.Patch(KVPatchOperation.Drop("/Party"));

        root.Log.Should().Equal("Party:Removed:COMPANY-><null>");
    }

    [Fact]
    public void NestedNodeSetter_InCode_NotifiesLikeInitAndDrop()
    {
        var root = this.CreateRoot<ScrRoot>();

        root.ChooseCompany();
        root.ClearParty();
        root.ClearParty();

        root.Log.Should().Equal("Party:Added:<null>->ScrCompany", "Party:Removed:COMPANY-><null>");
    }

    [Fact]
    public void NestedNodeSetter_InCode_AppliesDefaultsBeforeNotifying_LikeInit()
    {
        var root = this.CreateRoot<ScrRoot>();

        root.ChoosePerson();

        ((ScrPerson)root.Party!).Name.Should().Be("anonymous");
        root.Log.Should().Equal("Party:Added:<null>->ScrPerson(anonymous)");
    }

    [Fact]
    public void NestedNodeSetter_InCode_ReappliesDefaults_WhenTheSameTypeIsAssignedAgain()
    {
        var root = this.CreateRoot<ScrRoot>();
        root.ChoosePerson();
        ((ScrPerson)root.Party!).Name = "Ann";

        root.ChoosePerson();

        ((ScrPerson)root.Party!).Name.Should().Be("anonymous");
    }

    [Fact]
    public void NestedNodeSetter_InCode_RejectsValuesSetBeforeTheNodeIsAssigned()
    {
        var act = () => new ScrPerson { Name = "Ann" };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DefaultNestedType_NotifiesWhenTheAggregateIsBorn()
    {
        var root = this.CreateRoot<ScrDefaultedRoot>();

        root.ApplyDefaults();
        root.ApplyDefaults();

        root.Log.Should().Equal("Party:Added:<null>->ScrPerson(anonymous)");
    }

    [Fact]
    public void Move_NotifiesWithOldAndNewIndex_AndIsSilentWhenNothingMoves()
    {
        var root = this.CreateRoot<ScrRoot>();
        root.Items.Create("a");
        root.Items.Create("b");
        root.Items.Create("c");
        root.Log.Clear();

        root.Patch(KVPatchOperation.Move("/Items/c", 0));
        root.Items.MoveById("c", 0);
        root.Items.MoveById("a", 2);

        root.Items.GetActiveItemIds().Should().Equal("c", "b", "a");
        root.Log.Should().Equal("Items/c:Moved:2->0", "Items/a:Moved:1->2");
    }

    [Fact]
    public void ExistingNotifications_CarryTheirKind()
    {
        var root = this.CreateRoot<ScrRoot>();

        root.Title = "t";
        root.Items.Create("a");
        root.Items.RemoveById("a");

        root.Log.Should().Equal("Title:Value:<null>->t", "Items/a:Added:<null>->ScrItem", "Items/a:Removed:a-><null>");
    }

    [Fact]
    public void NestedNodeInsideACollectionItem_BubblesToTheRoot()
    {
        var root = this.CreateRoot<ScrRoot>();
        root.Items.Create("a");
        root.Log.Clear();

        root.Patch(KVPatchOperation.Init("/Items/a/Detail", "PERSON"));
        root.Patch(KVPatchOperation.Drop("/Items/a/Detail"));

        root.Log.Should().Equal("Items/a/Detail:Added:<null>->ScrPerson()", "Items/a/Detail:Removed:PERSON-><null>");
    }
}

public abstract partial class ScrTrackingRoot : KVRootNode
{
    public List<string> Log { get; } = [];

    protected void Record(string path, KVChangeKind kind, object? oldValue, object? newValue)
        => this.Log.Add($"{path}:{kind}:{Describe(oldValue)}->{Describe(newValue)}");

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        ScrPerson person => $"ScrPerson({person.Name})",
        KVNode node => node.GetType().Name,
        _ => value.ToString()!
    };
}

public partial class ScrRoot : ScrTrackingRoot
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Party")]
    public partial ScrParty? Party { get; private set; }

    [KVBind("Items")]
    public KVCollectionNode<ScrItem> Items { get; } = new();

    public void ChooseCompany() => this.Party = new ScrCompany();

    public void ChoosePerson() => this.Party = new ScrPerson();

    public void ClearParty() => this.Party = null;

    public void Track(KVChangeContext<ScrRoot> context)
        => this.Record(context.ChangedPath, context.Kind, context.OldValue, context.NewValue);
}

public partial class ScrDefaultedRoot : ScrTrackingRoot
{
    [KVBind("Party")]
    public partial ScrParty? Party { get; private set; }

    public void Track(KVChangeContext<ScrDefaultedRoot> context)
        => this.Record(context.ChangedPath, context.Kind, context.OldValue, context.NewValue);
}

public partial class ScrItem : KVCollectionItemNode
{
    [KVBind("Text")]
    public partial string? Text { get; set; }

    [KVBind("Detail")]
    public partial ScrParty? Detail { get; private set; }
}

public abstract partial class ScrParty : KVNestedNode;

public partial class ScrPerson : ScrParty
{
    [KVBind("Name")]
    public partial string? Name { get; set; }
}

public partial class ScrCompany : ScrParty
{
    [KVBind("CompanyName")]
    public partial string? CompanyName { get; set; }
}
