using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Binding;

// A tree that can grow without limit through collections: a part (collection item) has an empty nested
// slot; an ASSEMBLY in that slot holds parts again. The recursion closes over a nested node, which accepts
// a pre-built definition (SelfReference). Totals roll up level by level: each assembly reacts to its parts'
// cost and to their details' subtotal, so a change at a leaf reaches the root through cascading reactions.
public class RecursiveCollectionTreeTests
{
    internal static readonly KVNodeDefinition Definition = BuildDefinition();

    [Fact]
    public void Patch_BuildsAndReadsATreeFourLevelsDeep()
    {
        var root = CreateRoot();

        root.Patch(Branch(levels: 4, prefix: "p"));

        var part = root.Parts.GetById("p")!;
        for (var level = 2; level <= 4; level++)
        {
            part = ((TreeAssembly)part.Detail!).Parts.GetById($"{part.ItemId()}-1")!;
        }

        part.ItemId().Should().Be("p-1-1-1");
        part.Name.Should().Be("p-1-1-1");
        part.Detail.Should().BeNull();
    }

    [Fact]
    public void Reactions_RollUpThroughEveryLevel()
    {
        var root = CreateRoot();
        root.Patch(Branch(levels: 3, prefix: "p"));

        root.Patch(KVPatchOperation.Set("/Parts/p/Detail/Parts/p-1/Detail/Parts/p-1-1/Cost", 5m));

        // Each part costs 1 plus what its assembly holds: 5 at the leaf, 6 above, 7 at the top.
        root.Total.Should().Be(7m);
        ((TreeAssembly)root.Parts.GetById("p")!.Detail!).Subtotal.Should().Be(6m);
    }

    [Fact]
    public void Drop_RemovesTheSubtreeAndRecomputesTheTotal()
    {
        var root = CreateRoot();
        root.Patch(Branch(levels: 3, prefix: "p"));

        root.Patch(KVPatchOperation.Drop("/Parts/p/Detail/Parts/p-1/Detail"));

        root.Total.Should().Be(2m);
        ((TreeAssembly)root.Parts.GetById("p")!.Detail!).Parts.GetById("p-1")!.Detail.Should().BeNull();
    }

    // RESET checks the definition for PatchReadOnly members; on a recursive definition that walk must end.
    [Fact]
    public void Reset_OnARecursiveDefinition_Terminates_AndResetsTheItem()
    {
        var root = CreateRoot();
        root.Patch(Branch(levels: 3, prefix: "p"));
        root.Patch(KVPatchOperation.Set("/Parts/p/Cost", 9m));

        root.Patch(KVPatchOperation.Reset("/Parts/p"));

        var part = root.Parts.GetById("p")!;
        part.Cost.Should().Be(1m, "the default is re-applied");
        part.Detail.Should().BeNull();
        root.Total.Should().Be(1m);
    }

    [Fact]
    public void Commit_ThenRebind_ReadsTheDeepTreeBack()
    {
        var root = CreateRoot();
        root.Patch(Branch(levels: 4, prefix: "p"));
        var snapshot = root.Model.Overlay.Snapshot.Clone();
        snapshot.Apply(root.CreateCommit(DateTimeOffset.UtcNow));

        var rebound = KVRootNode.Create<TreeRoot>(KVOverlay.Create(snapshot, "bob"), Definition);

        rebound.Total.Should().Be(4m);
        var leaf = ((TreeAssembly)((TreeAssembly)((TreeAssembly)rebound.Parts.GetById("p")!.Detail!)
            .Parts.GetById("p-1")!.Detail!).Parts.GetById("p-1-1")!.Detail!).Parts.GetById("p-1-1-1")!;
        leaf.Name.Should().Be("p-1-1-1");
    }

    internal static TreeRoot CreateRoot() =>
        KVRootNode.CreateNew<TreeRoot>(KVOverlay.Create(new KVSnapshot(), "alice"), Definition);

    // One part per level, each but the last holding the next one in an ASSEMBLY.
    internal static KVPatchOperation[] Branch(int levels, string prefix)
    {
        var operations = new List<KVPatchOperation>();
        var collection = "/Parts";
        var id = prefix;
        for (var level = 1; level <= levels; level++)
        {
            operations.Add(KVPatchOperation.Add(collection, new KVAddPatchPayload(id)));
            operations.Add(KVPatchOperation.Set($"{collection}/{id}/Name", id));
            if (level < levels)
            {
                operations.Add(KVPatchOperation.Init($"{collection}/{id}/Detail", "ASSEMBLY"));
                collection = $"{collection}/{id}/Detail/Parts";
                id = $"{id}-1";
            }
        }

        return [.. operations];
    }

    internal static KVNodeDefinition BuildDefinition()
    {
        var assembly = new KVBindBuilder<TreeAssembly>();

        void Part(KVBindBuilder<TreePart> part)
        {
            part.Field(x => x.Name);
            part.Field(x => x.Cost, f => f.Default(1m));
            part.NestedNode(x => x.Detail, detail => detail
                .Bind<TreeAssembly>("ASSEMBLY", assembly.SelfReference)
                .Bind<TreeLeaf>("LEAF", leaf =>
                {
                    leaf.Field(x => x.Subtotal);
                    leaf.Field(x => x.Supplier);
                }));
        }

        assembly.Field(x => x.Subtotal);
        assembly.Collection(x => x.Parts, parts => parts.Item<TreePart>("PART", Part));
        assembly.OnChange(path => path.Collection(x => x.Parts).Field(x => x.Cost), x => x.Recalculate);
        assembly.OnChange(path => path.Collection(x => x.Parts).Any(), x => x.Recalculate);
        assembly.OnChange(path => path.Collection(x => x.Parts).NestedNode(x => x.Detail).Any(), x => x.Recalculate);
        assembly.OnChange(path => path.Collection(x => x.Parts).NestedNode(x => x.Detail).Field(x => x.Subtotal), x => x.Recalculate);
        assembly.Build();

        var root = new KVBindBuilder<TreeRoot>();
        root.Field(x => x.Total);
        root.Collection(x => x.Parts, parts => parts.Item<TreePart>("PART", Part));
        root.OnChange(path => path.Collection(x => x.Parts).Field(x => x.Cost), x => x.Recalculate);
        root.OnChange(path => path.Collection(x => x.Parts).Any(), x => x.Recalculate);
        root.OnChange(path => path.Collection(x => x.Parts).NestedNode(x => x.Detail).Any(), x => x.Recalculate);
        root.OnChange(path => path.Collection(x => x.Parts).NestedNode(x => x.Detail).Field(x => x.Subtotal), x => x.Recalculate);
        return root.Build();
    }
}

public partial class TreeRoot : KVRootNode
{
    [KVBind("Total")]
    public partial decimal Total { get; set; }

    [KVBind("Parts")]
    public KVCollectionNode<TreePart> Parts { get; } = new();

    public void Recalculate(KVChangeContext<TreeRoot> context) => this.Total = this.Parts.Sum(part => part.Rollup);
}

public partial class TreePart : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }

    [KVBind("Cost")]
    public partial decimal Cost { get; set; }

    [KVBind("Detail")]
    public partial TreeDetail? Detail { get; private set; }

    // The part's own cost plus everything its detail holds (computed, not stored).
    public decimal Rollup => this.Cost + (this.Detail?.Subtotal ?? 0m);
}

public abstract partial class TreeDetail : KVNestedNode
{
    [KVBind("Subtotal")]
    public partial decimal Subtotal { get; set; }
}

public partial class TreeAssembly : TreeDetail
{
    [KVBind("Parts")]
    public KVCollectionNode<TreePart> Parts { get; } = new();

    public void Recalculate(KVChangeContext<TreeAssembly> context) => this.Subtotal = this.Parts.Sum(part => part.Rollup);
}

public partial class TreeLeaf : TreeDetail
{
    [KVBind("Supplier")]
    public partial string? Supplier { get; set; }
}
