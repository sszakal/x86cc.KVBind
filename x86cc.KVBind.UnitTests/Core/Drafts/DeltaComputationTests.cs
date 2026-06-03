using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class DeltaComputationTests : KVModelTestBase
{
    public DeltaComputationTests()
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

        RegisterModelDefinition<NestedNodeRoot>(builder =>
        {
            builder.NestedNode(x => x.Animal, nested =>
            {
                nested.Bind<DogNestedNode>("DOG", dog => dog.Field(x => x.DogName, options => options.Required()));
                nested.Bind<CatNestedNode>("CAT", cat => cat.Field(x => x.CatName));
            });
        });
    }

    [Fact]
    public void DeltaComputation_WhenChildIsRemoved_EmitsSingleSyntheticRemovedDelta()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        var item = root.Items.Create(Guid.Parse("12300000-0000-0000-0000-000000000000"));
        item.Amount = 10;
        CommitSetup(model);
        root = CreateRoot<ChangeSetTestModel>(model);

        root.Items.RemoveById(item.ItemId()!);

        var changes = root.GetAllChanges();
        changes.Changes.Should().ContainSingle(d => d.Path.EndsWith("/12300000-0000-0000-0000-000000000000") && d.ChangeType == KVChangeDeltaType.Removed);
        changes.Changes.Should().NotContain(d => d.Path.Contains("Amount", StringComparison.Ordinal));

        root.Items.GetById(item.ItemId()!).Should().BeNull();
    }

    [Fact]
    public void DeltaComputation_WhenFieldIsRemoved_EmitsRemovedDelta()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.Status = 42;
        CommitSetup(model);
        root = CreateRoot<ChangeSetTestModel>(model);

        root.Patch(KVPatchOperation.Unset("/Status"));

        var changes = root.GetAllChanges();
        changes.Changes.Should().ContainSingle(d => d.Path == "Status" && d.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void DeltaComputation_WhenCollectionItemIsAdded_EmitsItemAddedWithoutMetadataPaths()
    {
        var root = CreateRoot<ChangeSetTestModel>();

        var item = root.Items.Create(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        item.Name = "added";

        var changes = root.GetAllChanges();
        changes.Changes.Should().Contain(d => d.Path.EndsWith("/00000000-0000-0000-0000-000000000002") && d.ChangeType == KVChangeDeltaType.Added);
        changes.Changes.Should().NotContain(d => d.Path.Contains("$", StringComparison.Ordinal));
    }

    [Fact]
    public void DeltaComputation_WhenCollectionItemMetadataChanges_DoesNotExposeMetadataPaths()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        var item = root.Items.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        item.Name = "base";
        CommitSetup(model);
        root = CreateRoot<ChangeSetTestModel>(model);
        var reloadedItem = root.Items.GetById(item.ItemId()!)!;

        reloadedItem.Name = "updated";

        var changes = root.GetAllChanges();
        changes.Changes.Should().ContainSingle(d => d.Path.EndsWith("/Name") && d.ChangeType == KVChangeDeltaType.Updated);
        changes.Changes.Should().NotContain(d => d.Path.Contains("$", StringComparison.Ordinal));
        changes.Changes.Should().NotContain(d => d.Path == $"Items/{item.ItemId()}");
    }

    [Fact]
    public void DeltaComputation_WhenNestedNodeTypeChanges_EmitsSlotDelta()
    {
        var root = CreateRoot<NestedNodeRoot>();

        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));

        var changes = root.GetAllChanges();
        changes.Changes.Should().ContainSingle(d => d.Path == "Animal" && d.ChangeType == KVChangeDeltaType.Added);
    }

    [Fact]
    public void DeltaComputation_WhenFieldValueIsUpdated_EmitsUpdatedDelta()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.Status = 42;
        CommitSetup(model);
        root = CreateRoot<ChangeSetTestModel>(model);

        root.Status = 99;

        var changes = root.GetAllChanges();
        changes.Changes.Should().ContainSingle(d => d.Path == "Status" && d.ChangeType == KVChangeDeltaType.Updated);
    }

    [Fact]
    public void DeltaComputation_WhenFieldGroupFieldIsUpdated_EmitsCanonicalPath()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<ChangeSetTestModel>(model);
        root.General.Code = "OLD";
        CommitSetup(model);
        root = CreateRoot<ChangeSetTestModel>(model);

        root.General.Code = "NEW";

        var changes = root.GetAllChanges();
        changes.Changes.Should().ContainSingle(d => d.Path == "General/Code" && d.ChangeType == KVChangeDeltaType.Updated);
    }

    [Fact]
    public void DeltaComputation_WhenNestedNodeTypeIsReplaced_EmitsUpdatedSlotDelta()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NestedNodeRoot>(model);
        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));
        CommitSetup(model);
        root = CreateRoot<NestedNodeRoot>(model);

        root.Patch(KVPatchOperation.Init("/Animal", "CAT")); // replace type

        var changes = root.GetAllChanges();
        changes.Changes.Should().ContainSingle(d => d.Path == "Animal" && d.ChangeType == KVChangeDeltaType.Updated);
    }
}
