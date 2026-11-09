using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class CollectionSeedTests : KVModelTestBase
{
    public CollectionSeedTests()
    {
        this.RegisterModelDefinition<SeedZoo>(builder =>
        {
            builder.Collection(x => x.Animals, animals => animals
                .Item<Animal>(item => item.Field(x => x.Name))
                .Item<Snake>(item =>
                {
                    item.Field(x => x.Name, f => f.Default("unnamed"));
                    item.Field(x => x.Venomous, f => f.Default(false));
                })
                .Item<Lizard>(item =>
                {
                    item.Field(x => x.Name);
                    item.Field(x => x.Legs, f => f.Default(4));
                })
                .Seed<Snake>()
                .Seed<Lizard>(l => l.Name = "Gecko")
                .Seed<Snake>("Python", s => s.Venomous = false));

            builder.Collection(x => x.Enclosures, enclosures => enclosures
                .Item<Enclosure>(enclosure => enclosure.Collection(x => x.Residents, residents => residents
                    .Item<Animal>(item => item.Field(x => x.Name))
                    .Item<Snake>(item =>
                    {
                        item.Field(x => x.Name, f => f.Default("unnamed"));
                        item.Field(x => x.Venomous);
                    })
                    .Seed<Snake>())));
        });
    }

    [Fact]
    public void ApplyDefaults_SeedsItemsUnderStableIdsInDeclarationOrder()
    {
        var first = this.CreateWithDefaults();
        var second = this.CreateWithDefaults();

        first.Animals.GetActiveItemIds().Should().Equal("Snake", "Lizard", "Python");
        second.Animals.GetActiveItemIds().Should().Equal(first.Animals.GetActiveItemIds());
        first.Animals.GetById("Snake").Should().BeOfType<Snake>();
        first.Animals.GetById("Lizard").Should().BeOfType<Lizard>();
        first.Animals.GetById("Python")!.ItemType().Should().Be("Snake");
    }

    [Fact]
    public void ApplyDefaults_RunsSeedConfigurationThenFillsRemainingFieldDefaults()
    {
        var root = this.CreateWithDefaults();

        var lizard = root.Animals.Get<Lizard>("Lizard")!;
        lizard.Name.Should().Be("Gecko");
        lizard.Legs.Should().Be(4);
        root.Animals.Get<Snake>("Snake")!.Name.Should().Be("unnamed");
    }

    [Fact]
    public void ApplyDefaults_KeepsExistingSeededItemsAndTheirValues()
    {
        var root = this.CreateWithDefaults();
        root.Animals.Get<Lizard>("Lizard")!.Legs = 3;
        root.CommitSetup();

        root.ApplyDefaults();

        root.Animals.GetActiveItemIds().Should().Equal("Snake", "Lizard", "Python");
        root.Animals.Get<Lizard>("Lizard")!.Legs.Should().Be(3);
    }

    [Fact]
    public void ApplyDefaults_DoesNotSeedACollectionThatAlreadyHasItems()
    {
        var root = this.CreateRoot<SeedZoo>();
        root.Animals.Create<Snake>("Snake");
        root.CommitSetup();

        root.ApplyDefaults();

        root.Animals.GetActiveItemIds().Should().Equal("Snake");
    }

    [Fact]
    public void ApplyDefaults_DoesNotBringBackASeededItemRemovedAndCommitted()
    {
        var root = this.CreateWithDefaults();
        root.CommitSetup();
        root.Animals.RemoveById("Lizard");
        root.CommitSetup();

        root.ApplyDefaults();

        root.Animals.GetActiveItemIds().Should().Equal("Snake", "Python");
    }

    [Fact]
    public void Create_AppliesTheNewItemsDeclaredDefaults()
    {
        var root = this.CreateRoot<SeedZoo>();

        var snake = root.Animals.Create<Snake>("Cobra");

        snake.Name.Should().Be("unnamed");
        root.Model.Overlay.TryGetDraftValue("Animals/Cobra/Name", out _).Should().BeTrue();
    }

    [Fact]
    public void AddPatch_AppliesTheNewItemsDeclaredDefaults()
    {
        var root = this.CreateWithDefaults();
        root.CommitSetup();

        root.Patch(KVPatchOperation.Add("/Animals", new KVAddPatchPayload("Iguana", "Lizard")));

        root.Animals.Get<Lizard>("Iguana")!.Legs.Should().Be(4);
    }

    [Fact]
    public void Create_SeedsCollectionsOwnedByTheNewItem()
    {
        var root = this.CreateRoot<SeedZoo>();

        var enclosure = root.Enclosures.Create("North");

        enclosure.Residents.GetActiveItemIds().Should().Equal("Snake");
        enclosure.Residents.Get<Snake>("Snake")!.Name.Should().Be("unnamed");
        root.Model.Overlay.TryGet("Enclosures/North/Residents/Snake/Name", out _).Should().BeTrue();
    }

    [Fact]
    public void Bind_OfAnExistingAggregate_NeverAppliesDefaults()
    {
        var root = this.CreateRoot<SeedZoo>();
        var snake = root.Animals.Create<Snake>("Cobra");
        snake.Name = null;
        root.CommitSetup();

        var reopened = KVRootNode.Create<SeedZoo>(
            new KVModelRoot(KVOverlay.Create(root.Model.Overlay.Snapshot, TestUser)),
            this.CreateRegistry().Get<SeedZoo>());

        reopened.Animals.Get<Snake>("Cobra")!.Name.Should().BeNull();
        reopened.Animals.GetActiveItemIds().Should().Equal("Cobra");
        reopened.Enclosures.Should().BeEmpty();
        reopened.Model.Overlay.Changes.Should().BeEmpty();
    }

    [Fact]
    public void Get_WhenStoredItemIsADifferentSubtype_Throws()
    {
        var root = this.CreateWithDefaults();

        root.Animals.Get<Snake>("Missing").Should().BeNull();
        var act = () => root.Animals.Get<Snake>("Lizard");

        act.Should().Throw<InvalidOperationException>().WithMessage("Collection item 'Lizard' is '*Lizard', not '*Snake'.");
    }

    [Fact]
    public void Patch_AddressesSeededItemsByStableCanonicalPath()
    {
        var root = this.CreateWithDefaults();

        root.Patch(
            KVPatchOperation.Set("/Animals/Snake/Venomous", true),
            KVPatchOperation.Add("/Animals", new KVAddPatchPayload("Iguana", "Lizard")));

        root.Animals.Get<Snake>("Snake")!.Venomous.Should().BeTrue();
        root.Animals.Get<Lizard>("Iguana").Should().NotBeNull();
        root.Model.Overlay.TryGet("Animals/Snake/Venomous", out _).Should().BeTrue();
    }

    [Fact]
    public void Create_WhenIdWasRemovedAfterCommit_YieldsAFreshItemWithoutResurrectedValues()
    {
        var root = this.CreateWithDefaults();
        root.Animals.Get<Lizard>("Lizard")!.Legs = 3;
        root.CommitSetup();

        root.Animals.RemoveById("Lizard");
        var recreated = root.Animals.Create<Lizard>("Lizard");

        recreated.Legs.Should().Be(4); // the declared default, not the committed 3
        recreated.Name.Should().BeNull(); // the committed "Gecko" is not resurrected
        recreated.ItemType().Should().Be("Lizard");
    }

    [Fact]
    public void RemoveThenRecreateSameId_IsReportedAsAnEditOfTheSameItem()
    {
        var root = this.CreateWithDefaults();
        root.Animals.Get<Lizard>("Lizard")!.Legs = 3; // Name is "Gecko" from the seed
        root.CommitSetup();

        root.Animals.RemoveById("Lizard");
        root.Animals.Create<Lizard>("Lizard");

        var changes = root.GetAllChanges().Changes;
        changes.Should().NotContain(d => d.Path == "Animals/Lizard");
        changes.Should().ContainSingle(d => d.Path == "Animals/Lizard/Legs" && d.ChangeType == KVChangeDeltaType.Updated);
        changes.Should().ContainSingle(d => d.Path == "Animals/Lizard/Name" && d.ChangeType == KVChangeDeltaType.Removed);

        root.CommitSetup();
        root.Animals.GetActiveItemIds().Should().Equal("Snake", "Python", "Lizard"); // re-added items append
        root.Model.Overlay.Snapshot.Data.Keys.Where(k => k.StartsWith("Animals/Lizard/", StringComparison.Ordinal))
            .Should().BeEquivalentTo("Animals/Lizard/$type", "Animals/Lizard/Legs");
    }

    [Theory]
    [InlineData("")]
    [InlineData("$items")]
    [InlineData("$type")]
    [InlineData("a/b")]
    [InlineData("has space")]
    public void Create_WhenIdIsInvalid_Throws(string itemId)
    {
        var root = this.CreateRoot<SeedZoo>();

        var act = () => root.Animals.Create<Snake>(itemId);

        act.Should().Throw<InvalidOperationException>().WithMessage("Collection item id '*' is invalid*");
    }

    [Fact]
    public void Create_WithGuid_StillUsesTheGuidAsTheItemId()
    {
        var root = this.CreateRoot<SeedZoo>();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        root.Animals.Create<Snake>(id);

        root.Animals.GetActiveItemIds().Should().Equal(id.ToString("D"));
    }

    [Fact]
    public void Definition_ExposesSeeds()
    {
        var definition = this.CreateRegistry().Get<SeedZoo>().FindCollection("Animals")!;

        definition.Seeds.Select(s => (s.ItemId, s.ItemDefinition.TypeToken))
            .Should().Equal(("Snake", "Snake"), ("Lizard", "Lizard"), ("Python", "Snake"));
    }

    [Fact]
    public void Build_WhenSeedIdIsDuplicated_Throws()
    {
        var builder = new KVBindBuilder<SeedZoo>();

        var act = () => builder.Collection(x => x.Animals, animals => animals
            .Item<Snake>(item => item.Field(x => x.Name))
            .Seed<Snake>()
            .Seed<Snake>("Snake"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Collection 'Animals' declares seed id 'Snake' more than once.");
    }

    [Fact]
    public void Build_WhenSeedIdIsInvalid_Throws()
    {
        var builder = new KVBindBuilder<SeedZoo>();

        var act = () => builder.Collection(x => x.Animals, animals => animals
            .Item<Snake>(item => item.Field(x => x.Name))
            .Seed<Snake>("$items"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Collection 'Animals' seed id '$items' is invalid*");
    }

    [Fact]
    public void Build_WhenSeedTypeIsNotDeclared_Throws()
    {
        var builder = new KVBindBuilder<SeedZoo>();

        var act = () => builder.Collection(x => x.Animals, animals => animals
            .Item<Snake>(item => item.Field(x => x.Name))
            .Seed<Lizard>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Collection 'Animals' seeds item type '*Lizard', which is not declared*");
    }

    [Fact]
    public void Build_WhenDefaultAndSeedAreCombined_Throws()
    {
        var builder = new KVBindBuilder<SeedZoo>();

        var act = () => builder.Collection(x => x.Animals, animals => animals
            .Item<Snake>(item => item.Field(x => x.Name))
            .Default(c => c.Create<Snake>())
            .Seed<Snake>());

        act.Should().Throw<InvalidOperationException>().WithMessage("Collection 'Animals' cannot combine Default(...) with Seed<TItem>(...).");
    }

    private SeedZoo CreateWithDefaults()
    {
        var root = this.CreateRoot<SeedZoo>();
        root.ApplyDefaults();
        return root;
    }
}

public partial class SeedZoo : KVRootNode
{
    [KVBind("Animals")]
    public KVCollectionNode<Animal> Animals { get; } = new();

    [KVBind("Enclosures")]
    public KVCollectionNode<Enclosure> Enclosures { get; } = new();
}

public partial class Enclosure : KVCollectionItemNode
{
    [KVBind("Residents")]
    public KVCollectionNode<Animal> Residents { get; } = new();
}

public partial class Animal : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }
}

public partial class Snake : Animal
{
    [KVBind("Venomous")]
    public partial bool Venomous { get; set; }
}

public partial class Lizard : Animal
{
    [KVBind("Legs")]
    public partial int Legs { get; set; }
}
