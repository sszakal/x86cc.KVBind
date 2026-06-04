using AwesomeAssertions;
using Meziantou.Framework.InlineSnapshotTesting;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class OverlayDraftLifecycleTests : KVModelTestBase
{
    public OverlayDraftLifecycleTests()
    {
        RegisterModelDefinition<ChangeSetTestModel>(modelBuilder =>
        {
            modelBuilder.Field(x => x.Title);
            modelBuilder.Field(x => x.Status);
            modelBuilder.FieldGroup(x => x.General, group =>
            {
                group.Field(x => x.Code);
                group.Field(x => x.Notes);
            });
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Item<ChangeSetItemNode>(item =>
                {
                    item.Field(x => x.Name);
                    item.Field(x => x.Amount);
                });
            });
        });
    }

    [Fact]
    public void Overlay_WhenDraftMutatesAcrossFieldGroupAndCollection_TracksSnapshotAndChanges()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<ChangeSetTestModel>(model);
        rootNode.Title = "source";
        rootNode.Status = 7;
        rootNode.General.Code = "SRC";
        rootNode.General.Notes = "Source notes";

        var sourceItem = rootNode.Items.Create();
        sourceItem.Name = "Source item";
        sourceItem.Amount = 10;
        var sourceItemId = rootNode.Items.GetItemId(sourceItem);

        CommitSetup(model);
        rootNode.Title = "overlay";
        rootNode.Status = 99;
        rootNode.General.Code = "OVR";
        rootNode.General.Notes = "Overlay notes";
        rootNode.Items.GetById(sourceItemId)!.Amount = 25;
        var addedOverlayItem = rootNode.Items.Create();
        addedOverlayItem.Name = "Overlay item";
        addedOverlayItem.Amount = 50;

        rootNode.Title.Should().Be("overlay");
        rootNode.Status.Should().Be(99);
        rootNode.General.Code.Should().Be("OVR");
        rootNode.General.Notes.Should().Be("Overlay notes");
        rootNode.Items.GetById(sourceItemId)!.Amount.Should().Be(25);
        rootNode.Items.Count().Should().Be(2);
        model.Snapshot.Data["Title"].Should().Be("source");
        
        rootNode.GetAllChanges().Changes.Should().Contain(change => change.Path == "Title" && change.ChangeType == KVChangeDeltaType.Updated);
    }

    [Fact]
    public void Overlay_WhenContinuingDraft_PreservesDraftValues()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<ChangeSetTestModel>(model);
        rootNode.Title = "base";

        CommitSetup(model);
        rootNode.Title = "draft";

        rootNode.Title.Should().Be("draft");
        rootNode.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == "Title" && change.ChangeType == KVChangeDeltaType.Updated);
    }

    [Fact]
    public void Discard_WhenOverlayHasFieldGroupAndCollectionChanges_RevertsToSnapshot()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<ChangeSetTestModel>(model);
        rootNode.Title = "base";
        rootNode.General.Code = "SRC";
        var sourceItem = rootNode.Items.Create();
        sourceItem.Name = "Base item";
        sourceItem.Amount = 100;
        var sourceItemId = rootNode.Items.GetItemId(sourceItem);

        CommitSetup(model);
        rootNode.Title = "changed";
        rootNode.General.Code = "OVR";
        rootNode.Items.GetById(sourceItemId)!.Amount = 101;
        var added = rootNode.Items.Create();
        added.Name = "new";
        added.Amount = 999;

        rootNode.Clear();
        
        rootNode.Title.Should().Be("base");
        rootNode.General.Code.Should().Be("SRC");
        rootNode.Items.GetById(sourceItemId)!.Amount.Should().Be(100);
        rootNode.GetAllChanges().Changes.Should().BeEmpty();
    }

    [Fact]
    public void Commit_WhenOverlayContainsFieldGroupAndCollectionChanges_AppliesChangesToSnapshot()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<ChangeSetTestModel>(model);
        rootNode.Title = "source";
        rootNode.Status = 7;
        rootNode.General.Code = "SRC";
        var item1 = rootNode.Items.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        item1.Name = "one";
        item1.Amount = 1;
        var item2 = rootNode.Items.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        item2.Name = "two";
        item2.Amount = 2;
        var id1 = rootNode.Items.GetItemId(item1);
        var id2 = rootNode.Items.GetItemId(item2);

        
        
        CommitSetup(model);
        rootNode.Title = "applied";
        rootNode.Status = 13;
        rootNode.General.Code = "APPLIED";
        rootNode.Items.GetById(id1)!.Amount = 11;
        rootNode.Items.RemoveById(id2);
        rootNode.Items.Create(Guid.Parse("33333333-3333-3333-3333-333333333333")).Name = "three";

        var commit = rootNode.CreateCommit(DateTimeOffset.UtcNow);
        model.Snapshot.Apply(commit);
        
        InlineSnapshot.Validate(model.Snapshot.Data
            .Where(pair => !pair.Key.Contains("/$") && !pair.Key.StartsWith("$", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Value), """
                                                Title: applied
                                                Status: 13
                                                General/Code: APPLIED
                                                Items/11111111-1111-1111-1111-111111111111/Name: one
                                                Items/11111111-1111-1111-1111-111111111111/Amount: 11
                                                Items/33333333-3333-3333-3333-333333333333/Name: three
                                                """);
    }

    [Fact]
    public void Discard_WhenGroupAndCollectionPathsArePatched_RevertsToSnapshot()
    {
        var model = new KVModelRoot();
        var source = CreateRoot<ChangeSetTestModel>(model);
        source.Title = "Base";
        source.General.Code = "SRC";
        var baseItem = source.Items.Create();
        baseItem.Amount = 1;
        var baseId = source.Items.GetItemId(baseItem);
        var addedId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        CommitSetup(model);
        source.Patch(KVPatchOperation.Set("/General/Code", "OVR"));
        source.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(addedId)));

        source.General.Code.Should().Be("OVR");
        source.Items.Count().Should().Be(2);

        source.Patch(KVPatchOperation.Discard("/General"));
        source.Patch(KVPatchOperation.Discard("/Items"));

        source.General.Code.Should().Be("SRC");
        source.Items.Count().Should().Be(1);
        source.Items.GetById(baseId).Should().NotBeNull();

        source.GetAllChanges().Changes.Should().BeEmpty();
    }

    [Fact]
    public void DeltaComputation_WhenDraftHasRootGroupAndCollectionChanges_ReturnsCanonicalPaths()
    {
        var model = new KVModelRoot();
        var source = CreateRoot<ChangeSetTestModel>(model);
        source.Title = "base";
        source.General.Code = "SRC";
        var sourceItem = source.Items.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        sourceItem.Name = "source-item";
        sourceItem.Amount = 10;
        var sourceItemId = source.Items.GetItemId(sourceItem);

        CommitSetup(model);
        source.Title = "updated";
        source.General.Code = "OVR";
        source.Items.GetById(sourceItemId)!.Amount = 25;
        var added = source.Items.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        added.Name = "added-item";
        added.Amount = 50;
        var removedId = sourceItemId;
        source.Items.RemoveById(removedId);

        var delta = source.GetAllChanges();
        
        InlineSnapshot.Validate(delta, """
            Changes:
              - Path: General/Code
                ChangeType: Updated
              - Path: Items/11111111-1111-1111-1111-111111111111
                ChangeType: Removed
              - Path: Items/22222222-2222-2222-2222-222222222222
              - Path: Title
                ChangeType: Updated
            """);
    }

    [Fact]
    public void Overlay_WhenFieldIsUnset_ClearsFieldAsDraftChange()
    {
        var model = new KVModelRoot();
        var source = CreateRoot<ChangeSetTestModel>(model);
        source.Title = "base";

        CommitSetup(model);
        source.Patch(KVPatchOperation.Unset("/Title"));

        source.Title.Should().BeNull();
        source.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == "Title" && change.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void Discard_WhenFieldWasUnset_RevealsSnapshotValue()
    {
        var model = new KVModelRoot();
        var source = CreateRoot<ChangeSetTestModel>(model);
        source.Title = "base";

        CommitSetup(model);
        source.Patch(KVPatchOperation.Unset("/Title"));
        source.Patch(KVPatchOperation.Discard("/Title"));

        source.Title.Should().Be("base");
        source.GetAllChanges().Changes.Should().BeEmpty();
    }
}

public partial class ChangeSetTestModel : KVRootNode
{
    [KVBind("Title")]
    public partial string Title { get; set; }

    [KVBind("Status")]
    public partial int Status { get; set; }

    [KVBind("General")]
    public ChangeSetGeneralGroup General { get; } = new();

    [KVBind("Items")]
    public KVCollectionNode<ChangeSetItemNode> Items { get; } = new();

}

public partial class ChangeSetGeneralGroup : KVFieldGroupNode
{
    [KVBind("Code")]
    public partial string Code { get; set; }

    [KVBind("Notes")]
    public partial string Notes { get; set; }
}

public partial class ChangeSetItemNode : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string Name { get; set; }

    [KVBind("Amount")]
    public partial int Amount { get; set; }
}
