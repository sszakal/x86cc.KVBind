using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class ResetTests : KVModelTestBase
{
    public ResetTests()
    {
        this.RegisterModelDefinition<ResetRoot>(builder =>
        {
            builder.Field(x => x.Title);

            builder.FieldGroup(x => x.Settings, settings => settings.Field(x => x.Theme, f => f.Default("dark")), group => group.Resettable());
            builder.FieldGroup(x => x.Locked, locked => locked.Field(x => x.Theme, f => f.Default("light")));

            builder.Collection(x => x.Animals, animals => animals
                .Item<ResetAnimal>(item => item.Field(x => x.Name))
                .Item<ResetSnake>(item =>
                {
                    item.Field(x => x.Name, f => f.Default("unnamed"));
                    item.Field(x => x.Venomous);
                })
                .Item<ResetLizard>(item =>
                {
                    item.Field(x => x.Name);
                    item.Field(x => x.Legs, f => f.Default(4));
                })
                .Seed<ResetSnake>("Snake")
                .Seed<ResetLizard>("Lizard")
                .Seed<ResetSnake>("Python"));

            builder.Collection(x => x.Enclosures, enclosures => enclosures
                .Item<ResetEnclosure>(enclosure =>
                {
                    enclosure.Field(x => x.Label);
                    enclosure.Collection(x => x.Residents, residents => residents
                        .Item<ResetAnimal>(item => item.Field(x => x.Name, f => f.Default("resident")))
                        .Seed<ResetAnimal>("First"));
                }));

            builder.Collection(x => x.Fixed, animals => animals
                .Item<ResetAnimal>(item => item.Field(x => x.Name, f => f.Default("fixed")))
                .Seed<ResetAnimal>("A")
                .AllowOperations(KVPatchOperations.Move));

            builder.Collection(x => x.Resettable, animals => animals
                .Item<ResetAnimal>(item => item.Field(x => x.Name, f => f.Default("fixed")))
                .Seed<ResetAnimal>("A")
                .AllowOperations(KVPatchOperations.Reset));

            builder.NestedNode(x => x.Owner, owner =>
            {
                owner.Bind<ResetPersonOwner>("Person", person =>
                {
                    person.Field(x => x.Name, f => f.Default("anonymous"));
                    person.Field(x => x.Email);
                });
                owner.Bind<ResetCompanyOwner>("Company", company => company.Field(x => x.CompanyName));
            });
        });
    }

    [Fact]
    public void ResetPatch_OnCollectionItem_ReplacesItsDataInPlaceAndKeepsItsPosition()
    {
        var root = this.CreateCommitted(r =>
        {
            var lizard = r.Animals.Get<ResetLizard>("Lizard")!;
            lizard.Name = "Gecko";
            lizard.Legs = 3;
        });

        root.Patch(KVPatchOperation.Reset("/Animals/Lizard"));

        root.Animals.GetActiveItemIds().Should().Equal("Snake", "Lizard", "Python");
        var reset = root.Animals.Get<ResetLizard>("Lizard")!;
        reset.ItemType().Should().Be("ResetLizard");
        reset.Name.Should().BeNull();
        reset.Legs.Should().Be(4);
    }

    [Fact]
    public void ResetPatch_OnCollectionItem_IsReportedAsFieldEditsOfTheSameItem()
    {
        var root = this.CreateCommitted(r =>
        {
            var lizard = r.Animals.Get<ResetLizard>("Lizard")!;
            lizard.Name = "Gecko";
            lizard.Legs = 3;
        });

        root.Patch(KVPatchOperation.Reset("/Animals/Lizard"));

        var changes = root.GetAllChanges().Changes;
        changes.Should().NotContain(d => d.Path == "Animals/Lizard" || d.Path == "Animals/$items");
        changes.Should().ContainSingle(d => d.Path == "Animals/Lizard/Legs" && d.ChangeType == KVChangeDeltaType.Updated);
        changes.Should().ContainSingle(d => d.Path == "Animals/Lizard/Name" && d.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void Reset_InCode_OnADraftOnlyItem_ReplacesItsData()
    {
        var root = this.CreateRoot<ResetRoot>();
        root.ApplyDefaults();
        var snake = root.Animals.Get<ResetSnake>("Snake")!;
        snake.Name = "Monty";
        snake.Venomous = true;

        snake.Reset();

        snake.Name.Should().Be("unnamed");
        snake.Venomous.Should().BeFalse();
        root.Animals.GetActiveItemIds().Should().Equal("Snake", "Lizard", "Python");
    }

    [Fact]
    public void ResetPatch_OnCollectionItem_ReseedsItsSubCollectionsAndDropsExtraItems()
    {
        var root = this.CreateCommitted(r =>
        {
            var north = r.Enclosures.Create("North");
            north.Label = "North wing";
            north.Residents.GetById("First")!.Name = "Renamed";
            north.Residents.Create("Extra");
        });

        root.Patch(KVPatchOperation.Reset("/Enclosures/North"));

        var enclosure = root.Enclosures.GetById("North")!;
        enclosure.Label.Should().BeNull();
        enclosure.Residents.GetActiveItemIds().Should().Equal("First");
        enclosure.Residents.GetById("First")!.Name.Should().Be("resident");
    }

    [Fact]
    public void ResetPatch_OnNestedNode_KeepsItsTypeAndReappliesDefaults()
    {
        var root = this.CreateCommitted(r =>
        {
            r.Patch(KVPatchOperation.Init("/Owner", "Person"));
            var person = (ResetPersonOwner)r.Owner!;
            person.Name = "Alice";
            person.Email = "alice@example.com";
        });

        root.Patch(KVPatchOperation.Reset("/Owner"));

        var reset = root.Owner.Should().BeOfType<ResetPersonOwner>().Subject;
        reset.Name.Should().Be("anonymous");
        reset.Email.Should().BeNull();
    }

    [Fact]
    public void ResetPatch_OnUninitializedNestedNode_Throws()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Reset("/Owner"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Nested node 'Owner' is not initialized and cannot be reset.");
    }

    [Fact]
    public void ResetPatch_OnResettableFieldGroup_ReappliesDefaults()
    {
        var root = this.CreateCommitted(r => r.Settings.Theme = "solarized");

        root.Patch(KVPatchOperation.Reset("/Settings"));

        root.Settings.Theme.Should().Be("dark");
    }

    [Fact]
    public void ResetPatch_OnFieldGroupNotMarkedResettable_Throws()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Reset("/Locked"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Field group 'Locked' is not resettable; declare it with Resettable().");
    }

    [Fact]
    public void ResetPatch_OnField_IsNotValid()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Reset("/Title"));

        act.Should().Throw<InvalidOperationException>().WithMessage("Patch operation 'RESET' is not valid for path '/Title'*");
    }

    [Fact]
    public void Reset_OnRoot_Throws()
    {
        var root = this.CreateCommitted();

        var act = root.Reset;

        act.Should().Throw<InvalidOperationException>().WithMessage("The root aggregate cannot be reset; use Clear() to drop draft changes.");
    }

    [Fact]
    public void ResetPatch_WhenCollectionDoesNotAllowReset_Throws()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Reset("/Fixed/A"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*collection 'Fixed' does not allow 'RESET'.");
    }

    [Fact]
    public void ResetPatch_WhenCollectionAllowsReset_Succeeds()
    {
        var root = this.CreateCommitted(r => r.Resettable.GetById("A")!.Name = "changed");

        root.Patch(KVPatchOperation.Reset("/Resettable/A"));

        root.Resettable.GetById("A")!.Name.Should().Be("fixed");
    }

    [Fact]
    public void ResetOperation_WithAValue_ThrowsOnConstruction()
    {
        var act = () => new KVPatchOperation(KVPatchOperations.Reset, "/Animals/Lizard", "value");

        act.Should().Throw<InvalidOperationException>().WithMessage("Patch operation 'RESET' does not accept values.");
    }

    private ResetRoot CreateCommitted(Action<ResetRoot>? arrange = null)
    {
        var root = this.CreateRoot<ResetRoot>();
        root.ApplyDefaults();
        arrange?.Invoke(root);
        root.CommitSetup();
        return root;
    }
}

public partial class ResetRoot : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Settings")]
    public ResetSettings Settings { get; } = new();

    [KVBind("Locked")]
    public ResetSettings Locked { get; } = new();

    [KVBind("Animals")]
    public KVCollectionNode<ResetAnimal> Animals { get; } = new();

    [KVBind("Enclosures")]
    public KVCollectionNode<ResetEnclosure> Enclosures { get; } = new();

    [KVBind("Fixed")]
    public KVCollectionNode<ResetAnimal> Fixed { get; } = new();

    [KVBind("Resettable")]
    public KVCollectionNode<ResetAnimal> Resettable { get; } = new();

    [KVBind("Owner")]
    public partial ResetOwner? Owner { get; private set; }
}

public partial class ResetSettings : KVFieldGroupNode
{
    [KVBind("Theme")]
    public partial string? Theme { get; set; }
}

public partial class ResetAnimal : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }
}

public partial class ResetSnake : ResetAnimal
{
    [KVBind("Venomous")]
    public partial bool Venomous { get; set; }
}

public partial class ResetLizard : ResetAnimal
{
    [KVBind("Legs")]
    public partial int Legs { get; set; }
}

public partial class ResetEnclosure : KVCollectionItemNode
{
    [KVBind("Label")]
    public partial string? Label { get; set; }

    [KVBind("Residents")]
    public KVCollectionNode<ResetAnimal> Residents { get; } = new();
}

public abstract partial class ResetOwner : KVNestedNode;

public partial class ResetPersonOwner : ResetOwner
{
    [KVBind("Name")]
    public partial string? Name { get; set; }

    [KVBind("Email")]
    public partial string? Email { get; set; }
}

public partial class ResetCompanyOwner : ResetOwner
{
    [KVBind("CompanyName")]
    public partial string? CompanyName { get; set; }
}
