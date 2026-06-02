using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class ProtectedNodePatchTests : KVModelTestBase
{
    public ProtectedNodePatchTests()
    {
        RegisterModelDefinition<NewPatchRootNode>(builder =>
        {
            builder.Field(x => x.Title);
            builder.FieldGroup(x => x.General, general => general.Field(x => x.Code));
            builder.Collection(x => x.Items, collection =>
            {
                collection.Item<NewPatchItemNode>(item => item.Field(x => x.Amount));
            });
        });
    }

    [Fact]
    public void ProtectedNodePatch_WhenSetTargetsFields_UpdatesRootGroupAndCollectionFields()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewPatchRootNode>(model);

        var item = root.Items.Create();
        var id = root.Items.GetItemId(item);

        root.CommitSetup();
        root.Patch(
            KVPatchOperation.Set("/Title", "Changed"),
            KVPatchOperation.Set("/General/Code", "OVR"),
            KVPatchOperation.Set($"/Items/{id}/Amount", 42));

        root.Title.Should().Be("Changed");
        root.General.Code.Should().Be("OVR");
        root.Items.GetById(id)!.Amount.Should().Be(42);
    }

    [Fact]
    public void ProtectedNodePatch_WhenCollectionOperationsTargetItems_AppliesAddRemoveAndMove()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewPatchRootNode>(model);

        var first = root.Items.Create();
        var second = root.Items.Create();
        var id1 = root.Items.GetItemId(first);
        var id2 = root.Items.GetItemId(second);
        var addedId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        root.CommitSetup();
        root.Patch(KVPatchOperation.Move($"/Items/{id2}", 0));
        root.Patch(KVPatchOperation.Remove($"/Items/{id1}"));
        root.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(addedId)));

        root.Items.Count().Should().Be(2);
        root.Items.GetItemId(root.Items.ElementAt(0)).Should().Be(id2);
        root.Items.GetById(id1).Should().BeNull();
        root.Items.GetById(addedId.ToString("D")).Should().NotBeNull();
    }

    [Fact]
    public void ProtectedNodePatch_WhenAddUsesUnsupportedSubtypeToken_Throws()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewPatchRootNode>(model);

        root.CommitSetup();
        var act = () => root.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(Guid.NewGuid(), "special")));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProtectedNodePatch_WhenDiscardTargetsField_RevertsFromSnapshot()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewPatchRootNode>(model);
        
        root.Title = "Base";

        root.CommitSetup();
        root.Patch(KVPatchOperation.Set("/Title", "Changed"));
        root.Title.Should().Be("Changed");

        root.Patch(KVPatchOperation.Discard("/Title"));
        root.Title.Should().Be("Base");
    }

    private sealed class NewPatchRootNode : KVRootNode
    {
        public NewPatchGeneralNode General { get; } = new();

        public NewPatchCollectionNode Items { get; } = new();

        public string? Title
        {
            get => GetField<string?>("Title");
            set => SetField("Title", value);
        }
        
    }

    private sealed class NewPatchGeneralNode : KVFieldGroupNode
    {
        public string? Code
        {
            get => GetField<string?>("Code");
            set => SetField("Code", value);
        }

    }

    private sealed class NewPatchCollectionNode : KVCollectionNode<NewPatchItemNode>;

    private class NewPatchItemNode : KVCollectionItemNode
    {
        public int Amount
        {
            get => GetField<int>("Amount");
            set => SetField("Amount", value);
        }
    }
}
