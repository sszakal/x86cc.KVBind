using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class CollectionAllowedOperationsTests : KVModelTestBase
{
    public CollectionAllowedOperationsTests()
    {
        this.RegisterModelDefinition<RestrictedZoo>(builder =>
        {
            // Fixed membership, reorderable; FEED is registered and listed, TAG is registered but not listed.
            builder.Collection(x => x.Fixed, animals => animals
                .Item<ZooAnimal>(item => item.Field(x => x.Name))
                .Seed<ZooAnimal>("Snake")
                .Seed<ZooAnimal>("Lizard")
                .Operation<string>("FEED", x => x.Feed)
                .Operation<string>("TAG", x => x.Tag)
                .AllowOperations(KVPatchOperations.Move, "feed"));

            builder.Collection(x => x.Locked, animals => animals
                .Item<ZooAnimal>(item => item.Field(x => x.Name))
                .Seed<ZooAnimal>("Snake")
                .Seed<ZooAnimal>("Lizard")
                .AllowOperations());

            builder.Collection(x => x.Open, animals => animals
                .Item<ZooAnimal>(item => item.Field(x => x.Name))
                .Seed<ZooAnimal>("Snake"));
        });
    }

    [Fact]
    public void Patch_WhenAddIsNotAllowed_Throws()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Add("/Fixed", new KVAddPatchPayload("Turtle")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Patch operation 'ADD' for '/Fixed' is not allowed: collection 'Fixed' does not allow 'ADD'.");
        root.Fixed.GetActiveItemIds().Should().Equal("Snake", "Lizard");
    }

    [Fact]
    public void Patch_WhenRemoveIsNotAllowed_Throws()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Remove("/Fixed/Snake"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Patch operation 'REMOVE' for '/Fixed/Snake' is not allowed: collection 'Fixed' does not allow 'REMOVE'.");
    }

    [Fact]
    public void Patch_WhenMoveIsAllowed_Reorders()
    {
        var root = this.CreateCommitted();

        root.Patch(KVPatchOperation.Move("/Fixed/Lizard", 0));

        root.Fixed.GetActiveItemIds().Should().Equal("Lizard", "Snake");
    }

    [Fact]
    public void Patch_WhenAllowOperationsIsEmpty_BlocksMove()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Move("/Locked/Lizard", 0));

        act.Should().Throw<InvalidOperationException>().WithMessage("*collection 'Locked' does not allow 'MOVE'.");
    }

    [Fact]
    public void Patch_FieldOperationsInsideRestrictedItems_AreUnaffected()
    {
        var root = this.CreateCommitted();

        root.Patch(
            KVPatchOperation.Set("/Locked/Snake/Name", "Monty"),
            KVPatchOperation.Discard("/Locked/Snake/Name"),
            KVPatchOperation.Set("/Locked/Lizard/Name", "Rex"));

        root.Locked.GetById("Snake")!.Name.Should().BeNull();
        root.Locked.GetById("Lizard")!.Name.Should().Be("Rex");
    }

    [Fact]
    public void Patch_CustomOperation_MustBeListedToBeAllowed()
    {
        var root = this.CreateCommitted();

        root.Patch(KVPatchOperation.Custom("FEED", "/Fixed", "Snake"));
        var act = () => root.Patch(KVPatchOperation.Custom("TAG", "/Fixed", "Snake"));

        root.Fed.Should().Equal("Snake");
        act.Should().Throw<InvalidOperationException>().WithMessage("*collection 'Fixed' does not allow 'TAG'.");
    }

    [Fact]
    public void Patch_UnrestrictedCollection_AllowsEverything()
    {
        var root = this.CreateCommitted();

        root.Patch(
            KVPatchOperation.Add("/Open", new KVAddPatchPayload("Turtle")),
            KVPatchOperation.Move("/Open/Turtle", 0),
            KVPatchOperation.Remove("/Open/Snake"));

        root.Open.GetActiveItemIds().Should().Equal("Turtle");
    }

    [Fact]
    public void CodeApi_IsNotRestricted()
    {
        var root = this.CreateCommitted();

        root.Locked.Create("Turtle");
        root.Locked.RemoveById("Snake").Should().BeTrue();

        root.Locked.GetActiveItemIds().Should().Equal("Lizard", "Turtle");
    }

    [Fact]
    public void Discard_WhenItWouldRemoveDraftOnlySeededItems_Throws()
    {
        var root = this.CreateRoot<RestrictedZoo>();
        root.ApplyDefaults(); // seeded in the draft, not committed

        var act = () => root.Patch(KVPatchOperation.Discard("/Fixed"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Patch operation 'DISCARD' for '/Fixed' is not allowed: collection 'Fixed' does not allow 'REMOVE'.");
    }

    [Fact]
    public void Discard_WhenItWouldRestoreARemovedItem_Throws()
    {
        var root = this.CreateCommitted();
        root.Fixed.RemoveById("Snake");

        var act = () => root.Patch(KVPatchOperation.Discard("/Fixed/$items"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*collection 'Fixed' does not allow 'ADD'.");
    }

    [Fact]
    public void Discard_WhenItWouldReorderAndMoveIsNotAllowed_Throws()
    {
        var root = this.CreateCommitted();
        root.Locked.MoveById("Lizard", 0);

        var act = () => root.Patch(KVPatchOperation.Discard("/"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*collection 'Locked' does not allow 'MOVE'.");
    }

    [Fact]
    public void Discard_WhenMembershipIsUnchangedOrOnlyReorderedWithMoveAllowed_Succeeds()
    {
        var root = this.CreateCommitted();
        root.Fixed.MoveById("Lizard", 0);
        root.Locked.GetById("Snake")!.Name = "Monty";

        root.Patch(KVPatchOperation.Discard("/"));

        root.Fixed.GetActiveItemIds().Should().Equal("Snake", "Lizard");
        root.Locked.GetById("Snake")!.Name.Should().BeNull();
    }

    [Fact]
    public void Definition_ExposesAllowedOperations()
    {
        var definition = this.CreateRegistry().Get<RestrictedZoo>();

        definition.FindCollection("Fixed")!.AllowedOperations.Should().BeEquivalentTo("MOVE", "FEED");
        definition.FindCollection("Fixed")!.IsOperationAllowed("add").Should().BeFalse();
        definition.FindCollection("Open")!.AllowedOperations.Should().BeNull();
        definition.FindCollection("Open")!.IsOperationAllowed(KVPatchOperations.Add).Should().BeTrue();
    }

    [Theory]
    [InlineData("SET")]
    [InlineData("DISCARD")]
    [InlineData("UNKNOWN")]
    public void Build_WhenAllowedOperationIsNotStructuralOrRegistered_Throws(string operation)
    {
        var builder = new KVBindBuilder<RestrictedZoo>();

        var act = () => builder.Collection(x => x.Open, animals => animals
            .Item<ZooAnimal>(item => item.Field(x => x.Name))
            .AllowOperations(operation));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Collection 'Open' allows operation '{operation}', which is neither ADD, REMOVE, MOVE, RESET nor a custom operation registered on this collection.");
    }

    private RestrictedZoo CreateCommitted()
    {
        var root = this.CreateRoot<RestrictedZoo>();
        root.ApplyDefaults();
        root.CommitSetup();
        return root;
    }
}

public partial class RestrictedZoo : KVRootNode
{
    [KVBind("Fixed")]
    public KVCollectionNode<ZooAnimal> Fixed { get; } = new();

    [KVBind("Locked")]
    public KVCollectionNode<ZooAnimal> Locked { get; } = new();

    [KVBind("Open")]
    public KVCollectionNode<ZooAnimal> Open { get; } = new();

    public List<string> Fed { get; } = [];

    public void Feed(string itemId) => this.Fed.Add(itemId);

    public void Tag(string itemId)
    {
        // Never reached: TAG is registered but not listed in AllowOperations, so the patch is rejected first.
    }
}

public partial class ZooAnimal : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }
}
