using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class NestedNodeBindingTests : KVModelTestBase
{
    public NestedNodeBindingTests()
    {
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
    public void NestedNode_WhenTypeIsMissing_ReturnsNullButInitializesSlotModel()
    {
        var root = CreateRoot<NestedNodeRoot>();

        root.Animal.Should().BeNull();
        root.Model.ChildModels.Should().ContainKey("Animal");
    }

    [Fact]
    public void NestedNode_WhenInitialized_SetsTypeAndBindsActiveNode()
    {
        var root = CreateRoot<NestedNodeRoot>();

        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));

        var dog = root.Animal.Should().BeOfType<DogNestedNode>().Subject;
        dog.ItemType().Should().Be("DOG");
        root.Model.ChildModels["Animal"].Get<string?>("$type").Should().Be("DOG");
        root.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == "Animal" && change.ChangeType == KVChangeDeltaType.Added);
    }

    [Fact]
    public void NestedNode_WhenInitialized_AllowsActiveNodeFieldEdits()
    {
        var root = CreateRoot<NestedNodeRoot>();

        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));
        root.Patch(KVPatchOperation.Set("/Animal/DogName", "Rex"));

        var dog = root.Animal.Should().BeOfType<DogNestedNode>().Subject;
        dog.DogName.Should().Be("Rex");
    }

    [Fact]
    public void NestedNode_WhenFieldIsSetBeforeInitialization_Throws()
    {
        var root = CreateRoot<NestedNodeRoot>();

        root.CommitSetup();
        var act = () => root.Patch(KVPatchOperation.Set("/Animal/DogName", "Rex"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NestedNode_WhenTypeIsReplaced_ClearsPreviousSubtreeAndClearDraftRestoresSnapshot()
    {
        var root = CreateRoot<NestedNodeRoot>();
        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "CAT"));
        root.Patch(KVPatchOperation.Set("/Animal/CatName", "Mittens"));
        root.CommitOverlay();

        var cat = root.Animal.Should().BeOfType<CatNestedNode>().Subject;
        cat.CatName.Should().Be("Mittens");

        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));

        root.Animal.Should().BeOfType<DogNestedNode>();
        root.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == "Animal" && change.ChangeType == KVChangeDeltaType.Updated);

        root.ClearDraft();

        cat = root.Animal.Should().BeOfType<CatNestedNode>().Subject;
        cat.CatName.Should().Be("Mittens");
    }

    [Fact]
    public void NestedNode_WhenDropped_ClearsSlotAndClearDraftRestoresSnapshot()
    {
        var root = CreateRoot<NestedNodeRoot>();
        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "CAT"));
        root.Patch(KVPatchOperation.Set("/Animal/CatName", "Mittens"));
        root.CommitOverlay();

        root.CommitSetup();
        root.Patch(KVPatchOperation.Drop("/Animal"));

        root.Animal.Should().BeNull();
        root.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == "Animal" && change.ChangeType == KVChangeDeltaType.Removed);

        root.ClearDraft();

        var cat = root.Animal.Should().BeOfType<CatNestedNode>().Subject;
        cat.CatName.Should().Be("Mittens");
    }

    [Fact]
    public void NestedNode_WhenTypeIsReplaced_DetachesPreviousRuntimeReference()
    {
        var root = CreateRoot<NestedNodeRoot>();
        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "CAT"));
        var cat = root.Animal.Should().BeOfType<CatNestedNode>().Subject;

        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));

        var act = () => cat.CatName = "stale";
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NestedNode_WhenValidated_TraversesActiveNode()
    {
        var root = CreateRoot<NestedNodeRoot>();
        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));

        var validation = root.Validate();

        validation.Errors.Should().ContainSingle(error => error.Path == "Animal/DogName" && error.Code == "required");
    }
}

public partial class NestedNodeRoot : KVRootNode
{
    [KVBind("Animal")]
    public partial AnimalNestedNode? Animal { get; private set; }
}

public abstract partial class AnimalNestedNode : KVNestedNode;

public partial class DogNestedNode : AnimalNestedNode
{
    [KVBind("DogName")]
    public partial string DogName { get; set; }
}

public partial class CatNestedNode : AnimalNestedNode
{
    [KVBind("CatName")]
    public partial string CatName { get; set; }
}
