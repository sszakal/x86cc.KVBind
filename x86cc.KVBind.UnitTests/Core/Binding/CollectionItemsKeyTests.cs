using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class CollectionItemsKeyTests : KVModelTestBase
{
    private static readonly Guid Id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Id3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public CollectionItemsKeyTests()
    {
        RegisterModelDefinition<ChangeSetTestModel>(builder =>
        {
            builder.Field(x => x.Title);
            builder.Field(x => x.Status);
            builder.FieldGroup(x => x.General, g =>
            {
                g.Field(x => x.Code); 
                g.Field(x => x.Notes);
            });
            builder.Collection(x => x.Items, items =>
                items.Item<ChangeSetItemNode>(item =>
                {
                    item.Field(x => x.Name);
                    item.Field(x => x.Amount);
                }));
        });
    }

    [Fact]
    public void Create_WritesItemsKey()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);

        root.Items.Create(Id1).Name = "first";

        model.Overlay.Changes.Should().ContainKey("Items/$items")
            .WhoseValue.Should().BeOfType<KVValue<string[]>>()
            .Which.TypedValue.Should().BeEquivalentTo(new[] { Id1.ToString("D") });
    }

    [Fact]
    public void CreateMultiple_MaintainsInsertionOrder()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.Items.Create(Id1);
        root.Items.Create(Id2);
        root.Items.Create(Id3);

        var ids = model.Overlay.Changes["Items/$items"].Should().BeOfType<KVValue<string[]>>()
            .Which.TypedValue!;
        ids.Should().BeEquivalentTo(
            new[] { Id1.ToString("D"), Id2.ToString("D"), Id3.ToString("D") },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void RemoveById_RemovesFromItemsKey()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.Items.Create(Id1);
        root.Items.Create(Id2);

        root.Items.RemoveById(Id1.ToString("D"));

        model.Overlay.Changes["Items/$items"].Should().BeOfType<KVValue<string[]>>()
            .Which.TypedValue.Should().BeEquivalentTo(new[] { Id2.ToString("D") });
    }

    [Fact]
    public void MoveById_UpdatesItemsKeyOrder()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.Items.Create(Id1);
        root.Items.Create(Id2);
        root.Items.Create(Id3);

        root.Items.MoveById(Id3.ToString("D"), 0);

        var ids = model.Overlay.Changes["Items/$items"].Should().BeOfType<KVValue<string[]>>()
            .Which.TypedValue!;
        ids.Should().BeEquivalentTo(
            new[] { Id3.ToString("D"), Id1.ToString("D"), Id2.ToString("D") },
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void ItemsOrder_SurvivesCommitAndRebind()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.Items.Create(Id1).Name = "first";
        root.Items.Create(Id2).Name = "second";
        root.Items.Create(Id3).Name = "third";
        root.Items.MoveById(Id3.ToString("D"), 0); // [Id3, Id1, Id2]

        CommitSetup(model);
        root = CreateRoot<ChangeSetTestModel>(model);

        root.Items.Count().Should().Be(3);
        root.Items.ElementAt(0).ItemId().Should().Be(Id3.ToString("D"));
        root.Items.ElementAt(1).ItemId().Should().Be(Id1.ToString("D"));
        root.Items.ElementAt(2).ItemId().Should().Be(Id2.ToString("D"));
    }

    [Fact]
    public void DeleteAndReAdd_SameId_FieldValuesRevertToSnapshot()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.Items.Create(Id1).Name = "original";
        CommitSetup(model);
        root = CreateRoot<ChangeSetTestModel>(model);

        root.Items.RemoveById(Id1.ToString("D"));
        var reAdded = root.Items.Create(Id1);
        reAdded.Name = "readded";

        root.Items.Count().Should().Be(1);
        root.Items.GetById(Id1.ToString("D"))!.Name.Should().Be("readded");

        // No Removed/Added for the item slot — just a field-level Updated
        var changes = root.GetAllChanges().Changes;
        changes.Should().NotContain(c => c.Path == $"Items/{Id1:D}" && c.ChangeType == KVChangeDeltaType.Removed);
        changes.Should().Contain(c => c.Path == $"Items/{Id1:D}/Name" && c.ChangeType == KVChangeDeltaType.Updated);
    }

    [Fact]
    public void ItemsKey_DoesNotAppearInGetAllChanges()
    {
        var root = CreateRoot<ChangeSetTestModel>();
        root.Items.Create(Id1).Name = "test";

        var changes = root.GetAllChanges().Changes;

        changes.Should().NotContain(c => c.Path.Contains("$items", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyCollection_HasNoItemsKey()
    {
        var model = new KVModelRoot();
        _ = CreateRoot<ChangeSetTestModel>(model);

        model.Overlay.Changes.Should().NotContainKey("Items/$items");
    }
}
