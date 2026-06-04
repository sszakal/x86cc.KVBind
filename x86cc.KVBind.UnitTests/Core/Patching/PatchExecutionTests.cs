using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class PatchExecutionTests : KVModelTestBase
{
    public PatchExecutionTests()
    {
        RegisterModelDefinition<ChangeSetPatchModel>(modelBuilder =>
        {
            modelBuilder.Field(x => x.Title);
            modelBuilder.FieldGroup(x => x.General, group => group.Field(x => x.Code));
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Item<ChangeSetPatchItemNode>(item =>
                {
                    item.Field(x => x.Name);
                    item.Field(x => x.Amount);
                });
            });
        });

        RegisterModelDefinition<PolymorphicPatchModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Item<ChangeSetPatchItemNode>(item =>
                {
                    item.Field(x => x.Name);
                    item.Field(x => x.Amount);
                });
            });
        });

        RegisterModelDefinition<NestedPatchModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.OuterItems, collection =>
            {
                collection.Item<NestedPatchOuterItemNode>(item =>
                {
                    item.Collection(x => x.InnerItems, innerCollection =>
                    {
                        innerCollection.Item<NestedPatchInnerItemNode>(innerItem =>
                        {
                            innerItem.Field(x => x.Name);
                            innerItem.Field(x => x.Amount);
                        });
                    });
                });
            });
        });

        RegisterModelDefinition<CustomOperationPatchModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Operation<ItemsToGroup>("GROUP", x => x.GroupItems);
                collection.Item<ChangeSetPatchItemNode>(item =>
                {
                    item.Field(x => x.Name);
                    item.Field(x => x.Amount);
                });
            });
        });

        RegisterModelDefinition<ConventionCustomOperationPatchModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Operation<ItemsToGroup>(x => x.GroupItems);
                collection.Item<ChangeSetPatchItemNode>(item => item.Field(x => x.Name));
            });
        });

        RegisterModelDefinition<NestedNodeRoot>(builder =>
        {
            builder.NestedNode(x => x.Animal, nested =>
                nested.Bind<DogNestedNode>("DOG", dog =>
                    dog.Field(x => x.DogName, options => options.Required())));
        });
    }

    [Fact]
    public void PatchExecution_WhenSetTargetsFields_UpdatesFieldAndGroupPaths()
    {
        var modelData = new KVModelRoot();
        var model = CreateRoot<ChangeSetPatchModel>(modelData);
        model.Title = "Base";
        model.General.Code = "SRC";

        model.CommitSetup();
        var result = model.Patch(
            KVPatchOperation.Set("/Title", "Changed"),
            KVPatchOperation.Set("/General/Code", "OVR"));

        model.Title.Should().Be("Changed");
        model.General.Code.Should().Be("OVR");
        result.Changes.Should().Contain(change => change.Path == "Title" && change.ChangeType == KVChangeDeltaType.Updated);
        result.Changes.Should().Contain(change => change.Path == "General/Code" && change.ChangeType == KVChangeDeltaType.Updated);
    }

    [Fact]
    public void PatchExecution_WhenCollectionOperationsTargetItems_AppliesAddRemoveAndMove()
    {
        var modelData = new KVModelRoot();
        var model = CreateRoot<ChangeSetPatchModel>(modelData);
        var first = model.Items.Create();
        first.Name = "first";
        first.Amount = 1;
        var second = model.Items.Create();
        second.Name = "second";
        second.Amount = 2;

        var firstId = model.Items.GetItemId(first);
        var secondId = model.Items.GetItemId(second);
        var addedId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        model.CommitSetup();
        model.Patch(KVPatchOperation.Move($"/Items/{secondId}", 0));
        model.Patch(KVPatchOperation.Remove($"/Items/{firstId}"));
        model.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(addedId)));

        model.Items.GetById(firstId).Should().BeNull();
        model.Items.GetById(addedId.ToString("D")).Should().NotBeNull();
        model.Items.Count().Should().Be(2);
        model.Items.GetItemId(model.Items.ElementAt(0)).Should().Be(secondId);
    }

    [Fact]
    public void PatchOperation_WhenAddPayloadIsMissing_ThrowsDuringCreation()
    {
        var act = () => new KVPatchOperation(KVPatchOperationType.Add, "/Items");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchExecution_WhenAddUsesDuplicateClientItemId_Throws()
    {
        var model = CreateRoot<ChangeSetPatchModel>();
        var itemId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        model.CommitSetup();
        model.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(itemId)));
        var act = () => model.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(itemId)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchExecution_WhenAddUsesUnsupportedSubtypeToken_Throws()
    {
        var modelData = new KVModelRoot();
        var model = CreateRoot<PolymorphicPatchModel>(modelData);
        model.CommitSetup();

        var act = () => model.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(Guid.NewGuid(), "special")));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchExecution_WhenDiscardTargetsField_RevertsPathFromSnapshot()
    {
        var model = CreateRoot<ChangeSetPatchModel>();
        model.Title = "Base";

        model.CommitSetup();
        model.Patch(KVPatchOperation.Set("/Title", "Changed"));

        model.Title.Should().Be("Changed");

        model.Patch(KVPatchOperation.Discard("/Title"));

        model.Title.Should().Be("Base");
    }

    [Fact]
    public void PatchExecution_WhenDiscardTargetsRoot_ClearsDraftAndAllowsLaterEdits()
    {
        var model = CreateRoot<ChangeSetPatchModel>();
        model.Title = "Base";

        model.CommitSetup();
        model.Title = "Draft";

        model.Patch(KVPatchOperation.Discard("/"));
        model.Patch(KVPatchOperation.Set("/Title", "After discard"));

        model.Title.Should().Be("After discard");
        model.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == "Title" && change.ChangeType == KVChangeDeltaType.Updated);
    }

    [Fact]
    public void PatchExecution_WhenItemIsRemovedBeforeLaterEdit_Throws()
    {
        var model = CreateRoot<ChangeSetPatchModel>();
        var item = model.Items.Create();
        item.Amount = 1;
        var itemId = model.Items.GetItemId(item);

        model.CommitSetup();
        model.Patch(KVPatchOperation.Remove($"/Items/{itemId}"));
        var act = () => model.Patch(KVPatchOperation.Set($"/Items/{itemId}/Amount", 42));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchExecution_WhenItemIsEditedBeforeRemove_LeavesItemRemoved()
    {
        var model = CreateRoot<ChangeSetPatchModel>();
        var item = model.Items.Create();
        item.Amount = 1;
        var itemId = model.Items.GetItemId(item);

        model.CommitSetup();
        model.Patch(KVPatchOperation.Set($"/Items/{itemId}/Amount", 42));
        model.Patch(KVPatchOperation.Remove($"/Items/{itemId}"));

        model.Items.GetById(itemId).Should().BeNull();
        model.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == $"Items/{itemId}" && change.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void PatchExecution_WhenSetTargetsNestedCollectionItem_TraversesNestedCollections()
    {
        var model = CreateRoot<NestedPatchModel>();
        var outerItem = model.OuterItems.Create();
        var outerId = model.OuterItems.GetItemId(outerItem);
        var innerItem = outerItem.InnerItems.Create();
        var innerId = outerItem.InnerItems.GetItemId(innerItem);

        model.CommitSetup();
        model.Patch(
            KVPatchOperation.Set($"/OuterItems/{outerId}/InnerItems/{innerId}/Name", "nested"),
            KVPatchOperation.Set($"/OuterItems/{outerId}/InnerItems/{innerId}/Amount", 9));

        var patchedInnerItem = model.OuterItems.GetById(outerId)!.InnerItems.GetById(innerId);
        patchedInnerItem.Should().NotBeNull();
        patchedInnerItem!.Name.Should().Be("nested");
        patchedInnerItem.Amount.Should().Be(9);
    }

    [Fact]
    public void PatchExecution_WhenSelectorPathIsUsed_Throws()
    {
        var model = CreateRoot<ChangeSetPatchModel>();
        var item = model.Items.Create();
        item.Name = "entry-1";
        item.Amount = 10;

        model.CommitSetup();
        var act = () => model.Patch(KVPatchOperation.Set("/Items(Name='entry-1')/Amount", 99));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchExecution_WhenCustomCollectionOperationIsRegistered_InvokesHandler()
    {
        var model = CreateRoot<CustomOperationPatchModel>();
        var itemId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        model.CommitSetup();
        model.Patch(KVPatchOperation.Custom("GROUP", "/Items", new ItemsToGroup(itemId)));

        model.GroupInvocations.Should().Be(1);
        model.Items.GetById(itemId.ToString("D")).Should().NotBeNull();
    }

    [Fact]
    public void PatchExecution_WhenCustomCollectionOperationIsUnregistered_Throws()
    {
        var model = CreateRoot<ChangeSetPatchModel>();

        model.CommitSetup();
        var act = () => model.Patch(KVPatchOperation.Custom("GROUP", "/Items", new ItemsToGroup(Guid.NewGuid())));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchExecution_WhenCustomOperationUsesMethodNameConvention_InvokesHandler()
    {
        var model = CreateRoot<ConventionCustomOperationPatchModel>();
        var itemId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        model.CommitSetup();
        model.Patch(KVPatchOperation.Custom("GROUPITEMS", "/Items", new ItemsToGroup(itemId)));

        model.Items.GetById(itemId.ToString("D")).Should().NotBeNull();
    }

    [Fact]
    public void PatchExecution_WhenCustomOperationRunsBeforeBuiltInOperation_AppliesSequentially()
    {
        var model = CreateRoot<CustomOperationPatchModel>();
        var itemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var itemPath = itemId.ToString("D");

        model.CommitSetup();
        model.Patch(
            KVPatchOperation.Custom("GROUP", "/Items", new ItemsToGroup(itemId)),
            KVPatchOperation.Set($"/Items/{itemPath}/Name", "Grouped"));

        model.Items.GetById(itemPath)!.Name.Should().Be("Grouped");
    }

    [Fact]
    public void PatchExecution_WhenCustomOperationUsesBuiltInName_ThrowsDuringDefinitionBuild()
    {
        RegisterModelDefinition<BuiltInOverridePatchModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Operation<ItemsToGroup>(KVPatchOperations.Set, x => x.GroupItems);
                collection.Item<ChangeSetPatchItemNode>(item => item.Field(x => x.Name));
            });
        });

        Action act = () => CreateRoot<BuiltInOverridePatchModel>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchExecution_WhenUnsetTargetsCommittedField_RemovesFieldAndEmitsRemovedDelta()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetPatchModel>(model);
        root.Title = "original";
        CommitSetup(model);
        root = CreateRoot<ChangeSetPatchModel>(model);

        var result = root.Patch(KVPatchOperation.Unset("/Title"));

        root.Title.Should().BeNull();
        result.Changes.Should().Contain(c => c.Path == "Title" && c.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void PatchExecution_WhenUnsetTargetsDraftOnlyField_ProducesNoDelta()
    {
        var root = CreateRoot<ChangeSetPatchModel>();
        root.Title = "draft-only";

        var result = root.Patch(KVPatchOperation.Unset("/Title"));

        root.Title.Should().BeNull();
        result.Changes.Should().NotContain(c => c.Path == "Title");
    }

    [Fact]
    public void PatchExecution_WhenDropTargetsInitializedNestedNode_ClearsSlotAndEmitsRemovedDelta()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NestedNodeRoot>(model);
        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));
        CommitSetup(model);
        root = CreateRoot<NestedNodeRoot>(model);

        var result = root.Patch(KVPatchOperation.Drop("/Animal"));

        root.Animal.Should().BeNull();
        result.Changes.Should().Contain(c => c.Path == "Animal" && c.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void PatchExecution_WhenDropTargetsUninitializedNestedNode_IsNoOp()
    {
        var root = CreateRoot<NestedNodeRoot>();

        var result = root.Patch(KVPatchOperation.Drop("/Animal"));

        root.Animal.Should().BeNull();
        result.Changes.Should().BeEmpty();
    }
}

public partial class ChangeSetPatchModel : KVRootNode
{
    [KVBind("Title")]
    public partial string Title { get; set; }

    [KVBind("General")]
    public ChangeSetPatchGeneralNode General { get; } = new();

    [KVBind("Items")]
    public KVCollectionNode<ChangeSetPatchItemNode> Items { get; } = new();
}

public partial class ChangeSetPatchGeneralNode : KVFieldGroupNode
{
    [KVBind("Code")]
    public partial string Code { get; set; }
}

public partial class ChangeSetPatchItemNode : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string Name { get; set; }

    [KVBind("Amount")]
    public partial int Amount { get; set; }
}

public partial class CustomOperationPatchModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<ChangeSetPatchItemNode> Items { get; } = new();

    public int GroupInvocations { get; private set; }

    public void GroupItems(ItemsToGroup argument)
    {
        Items.Create(argument.ItemId);
        GroupInvocations++;
    }
}

public partial class ConventionCustomOperationPatchModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<ChangeSetPatchItemNode> Items { get; } = new();

    public void GroupItems(ItemsToGroup argument)
    {
        Items.Create(argument.ItemId);
    }
}

public partial class BuiltInOverridePatchModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<ChangeSetPatchItemNode> Items { get; } = new();

    public void GroupItems(ItemsToGroup argument)
    {
    }
}

public sealed record ItemsToGroup(Guid ItemId);

public partial class PolymorphicPatchModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<ChangeSetPatchItemNode> Items { get; } = new();
}

public partial class SpecialChangeSetPatchItemNode : ChangeSetPatchItemNode
{
    [KVBind("Subtype")]
    public partial string Subtype { get; set; }
}

public partial class NestedPatchModel : KVRootNode
{
    [KVBind("OuterItems")]
    public KVCollectionNode<NestedPatchOuterItemNode> OuterItems { get; } = new();
}

public partial class NestedPatchOuterItemNode : KVCollectionItemNode
{
    [KVBind("InnerItems")]
    public KVCollectionNode<NestedPatchInnerItemNode> InnerItems { get; } = new();
}

public partial class NestedPatchInnerItemNode : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string Name { get; set; }

    [KVBind("Amount")]
    public partial int Amount { get; set; }
}
