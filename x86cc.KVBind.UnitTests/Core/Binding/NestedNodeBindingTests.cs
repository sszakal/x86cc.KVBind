using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

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
    }

    [Fact]
    public void NestedNode_WhenInitialized_SetsTypeAndBindsActiveNode()
    {
        var root = CreateRoot<NestedNodeRoot>();

        root.CommitSetup();
        root.Patch(KVPatchOperation.Init("/Animal", "DOG"));

        var dog = root.Animal.Should().BeOfType<DogNestedNode>().Subject;
        dog.ItemType().Should().Be("DOG");
        root.Model.Overlay.TryGet("Animal/$type", out var typeVal).Should().BeTrue();
        typeVal!.Value.Should().Be("DOG");
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

// ── Recursive nested node tests ───────────────────────────────────────────────

public class RecursiveNestedNodeTests
{
    // SelfReference exposes the definition being built without locking the builder,
    // enabling self-referential declarations. Activation is lazy so the circular
    // reference in the definition graph is safe.
    private static KVNodeDefinition BuildTreeNodeDefinition()
    {
        var nodeBuilder = new KVBindBuilder<BranchNode>();
        nodeBuilder.Field(x => x.Value);
        nodeBuilder.NestedNode(x => x.Child, child =>
            child.Bind<BranchNode>("BRANCH", nodeBuilder.SelfReference));
        return nodeBuilder.Build();
    }

    private static TreeRoot CreateRoot()
    {
        var nodeDef = BuildTreeNodeDefinition();
        var rootBuilder = new KVBindBuilder<TreeRoot>();
        rootBuilder.NestedNode(x => x.Root, root => root.Bind<BranchNode>("BRANCH", nodeDef));
        var rootDef = rootBuilder.Build();

        var model = new KVModelRoot(KVOverlay.Create(new KVSnapshot(), "test"));
        return KVRootNode.Create<TreeRoot>(model, rootDef);
    }

    [Fact]
    public void RecursiveGraph_WhenRootINITed_Level1ExistsAndLevel2IsNull()
    {
        var root = CreateRoot();

        root.Patch(KVPatchOperation.Init("/Root", "BRANCH"));

        var level1 = root.Root.Should().BeOfType<BranchNode>().Subject;
        level1.Child.Should().BeNull();
    }

    [Fact]
    public void RecursiveGraph_WhenTwoLevelsINITed_BothLevelsExistAndLevel3IsNull()
    {
        var root = CreateRoot();

        root.Patch(KVPatchOperation.Init("/Root", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Value", "level-1"));
        root.Patch(KVPatchOperation.Init("/Root/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Value", "level-2"));

        var level1 = root.Root.Should().BeOfType<BranchNode>().Subject;
        level1.Value.Should().Be("level-1");

        var level2 = level1.Child.Should().BeOfType<BranchNode>().Subject;
        level2.Value.Should().Be("level-2");
        level2.Child.Should().BeNull();
    }

    [Fact]
    public void RecursiveGraph_WhenThreeLevelsINITed_AllThreeLevelsAccessible()
    {
        var root = CreateRoot();

        root.Patch(KVPatchOperation.Init("/Root", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Value", "L1"));
        root.Patch(KVPatchOperation.Init("/Root/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Value", "L2"));
        root.Patch(KVPatchOperation.Init("/Root/Child/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Child/Value", "L3"));

        var l1 = root.Root.Should().BeOfType<BranchNode>().Subject;
        var l2 = l1.Child.Should().BeOfType<BranchNode>().Subject;
        var l3 = l2.Child.Should().BeOfType<BranchNode>().Subject;

        l1.Value.Should().Be("L1");
        l2.Value.Should().Be("L2");
        l3.Value.Should().Be("L3");
        l3.Child.Should().BeNull();
    }

    [Fact]
    public void RecursiveGraph_WhenMiddleLevelDropped_SubtreeRemovedButParentIntact()
    {
        var root = CreateRoot();

        root.Patch(KVPatchOperation.Init("/Root", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Value", "L1"));
        root.Patch(KVPatchOperation.Init("/Root/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Value", "L2"));
        root.Patch(KVPatchOperation.Init("/Root/Child/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Child/Value", "L3"));

        root.Patch(KVPatchOperation.Drop("/Root/Child"));

        var l1 = root.Root.Should().BeOfType<BranchNode>().Subject;
        l1.Value.Should().Be("L1");
        l1.Child.Should().BeNull(); // subtree gone
    }

    [Fact]
    public void RecursiveGraph_DeltaComputation_NewSlotRollsUpToSingleAddedDelta()
    {
        // When a nested node slot is first INITed in a draft (no snapshot yet),
        // the entire subtree is rolled up into a single slot-level Added delta.
        // Individual field deltas inside the newly-added node are suppressed.
        var root = CreateRoot();

        root.Patch(KVPatchOperation.Init("/Root", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Value", "L1"));
        root.Patch(KVPatchOperation.Init("/Root/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Value", "L2"));

        var changes = root.GetAllChanges().Changes;

        // The whole "Root" subtree is new → single Added delta, not individual fields
        changes.Should().ContainSingle(c => c.Path == "Root" && c.ChangeType == KVChangeDeltaType.Added);
        changes.Should().NotContain(c => c.Path == "Root/Value");
        changes.Should().NotContain(c => c.Path == "Root/Child");
    }

    [Fact]
    public void RecursiveGraph_DeltaComputation_ChildNodeAddedAfterCommit_ReportsChildPath()
    {
        // After committing the root node, adding a child produces a distinct delta for that child.
        var nodeBuilder = new KVBindBuilder<BranchNode>();
        nodeBuilder.Field(x => x.Value);
        nodeBuilder.NestedNode(x => x.Child, child =>
            child.Bind<BranchNode>("BRANCH", nodeBuilder.SelfReference));
        var nodeDef = nodeBuilder.Build();

        var rootBuilder = new KVBindBuilder<TreeRoot>();
        rootBuilder.NestedNode(x => x.Root, r => r.Bind<BranchNode>("BRANCH", nodeDef));
        var rootDef = rootBuilder.Build();

        var snapshot = new KVSnapshot();
        var overlay = KVOverlay.Create(snapshot, "test");
        var model = new KVModelRoot(overlay);
        var root = KVRootNode.Create<TreeRoot>(model, rootDef);

        // Commit with Root and Value, but no Child yet
        root.Patch(KVPatchOperation.Init("/Root", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Value", "L1"));
        snapshot.Apply(model.Overlay.ToCommit(DateTimeOffset.UtcNow));
        model.ReplaceOverlay(KVOverlay.Create(snapshot, "test"));

        // Now INIT the child in a new draft
        root.Patch(KVPatchOperation.Init("/Root/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Value", "L2"));

        var changes = root.GetAllChanges().Changes;

        // Root is already committed — only Root/Child is new
        changes.Should().Contain(c => c.Path == "Root/Child" && c.ChangeType == KVChangeDeltaType.Added);
        changes.Should().NotContain(c => c.Path == "Root");
    }

    [Fact]
    public void RecursiveGraph_CommitAndRebind_FullTreeSurvivesRoundTrip()
    {
        var root = CreateRoot();

        root.Patch(KVPatchOperation.Init("/Root", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Value", "L1"));
        root.Patch(KVPatchOperation.Init("/Root/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Value", "L2"));
        root.Patch(KVPatchOperation.Init("/Root/Child/Child", "BRANCH"));
        root.Patch(KVPatchOperation.Set("/Root/Child/Child/Value", "L3"));

        var nodeDef = BuildTreeNodeDefinition();
        var rootBuilder = new KVBindBuilder<TreeRoot>();
        rootBuilder.NestedNode(x => x.Root, r => r.Bind<BranchNode>("BRANCH", nodeDef));
        var rootDef = rootBuilder.Build();

        var snapshot = new KVSnapshot();
        var overlay = KVOverlay.Create(snapshot, "test");
        var original = KVRootNode.Create<TreeRoot>(new KVModelRoot(overlay), rootDef);

        original.Patch(KVPatchOperation.Init("/Root", "BRANCH"));
        original.Patch(KVPatchOperation.Set("/Root/Value", "L1"));
        original.Patch(KVPatchOperation.Init("/Root/Child", "BRANCH"));
        original.Patch(KVPatchOperation.Set("/Root/Child/Value", "L2"));
        original.Patch(KVPatchOperation.Init("/Root/Child/Child", "BRANCH"));
        original.Patch(KVPatchOperation.Set("/Root/Child/Child/Value", "L3"));

        var commit = original.CreateCommit(DateTimeOffset.UtcNow);
        snapshot.Apply(commit);

        var reloaded = KVRootNode.Create<TreeRoot>(
            new KVModelRoot(KVOverlay.Create(snapshot.Clone(), "test")), rootDef);

        var l1 = reloaded.Root.Should().BeOfType<BranchNode>().Subject;
        var l2 = l1.Child.Should().BeOfType<BranchNode>().Subject;
        var l3 = l2.Child.Should().BeOfType<BranchNode>().Subject;
        l1.Value.Should().Be("L1");
        l2.Value.Should().Be("L2");
        l3.Value.Should().Be("L3");
    }
}

// ── Recursive tree model ───────────────────────────────────────────────────────

public partial class TreeRoot : KVRootNode
{
    [KVBind("Root")]
    public partial TreeNode? Root { get; private set; }
}

public abstract partial class TreeNode : KVNestedNode;

public partial class BranchNode : TreeNode
{
    [KVBind("Value")]
    public partial string? Value { get; set; }

    [KVBind("Child")]
    public partial TreeNode? Child { get; private set; }
}
