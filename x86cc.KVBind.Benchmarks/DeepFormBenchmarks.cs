using BenchmarkDotNet.Attributes;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.DeepForms;

namespace x86cc.KVBind.Benchmarks;

// The stages of one API request on the recursive component tree (`deep`), with about 1,000 field values at
// depth 1, 2, 3 and 4 and the same 45 (42) components, so only the depth changes (DeepFormShape):
//   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*DeepForm*"
// Each benchmark binds its own overlay, as a request does, except Validate/Deltas/Commit which reuse one.
[MemoryDiagnoser]
[ShortRunJob]
public class DeepFormBenchmarks
{
    private const int ChangedFields = 100;

    private IAggregateModel model = null!;
    private KVSnapshot committed = null!;
    private KVPatchOperation[] build = null!;
    private KVPatchOperation[] everyField = null!;
    private KVPatchOperation[] deepestNote = null!;
    private KVPatchOperation[] deepestPrice = null!;
    private KVPatchOperation[] everyLeafCost = null!;
    private KVPatchOperation[] dropFirstSubtree = null!;
    private KVOverlay changedOverlay = null!;
    private KVRootNode changedRoot = null!;
    private KVSnapshot upstream = null!;
    private KVCommit upstreamCommit = null!;

    [Params(1, 2, 3, 4)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.model = SampleSetup.CreateCatalog(out _).Find(DeepFormModels.Name)!;
        var shape = DeepFormShape.AtDepth(this.Depth);

        this.build = [KVPatchOperation.Set("/Title", "Tree"), .. DeepFormFiller.Build(shape)];
        var overlay = KVOverlay.Create(this.model.Definition.NewSnapshot(), "setup");
        var root = this.model.BindNew(overlay);
        root.Patch(this.build);
        this.committed = overlay.Snapshot.Clone();
        this.committed.Apply(root.CreateCommit(DateTimeOffset.UtcNow));

        var leaf = DeepFormFiller.DeepestLeaf(shape);
        this.everyField = DeepFormFiller.SetAll(shape, variant: 1).ToArray();
        var hundredChanges = SpreadOver(this.everyField, ChangedFields);
        this.deepestNote = [KVPatchOperation.Set($"{leaf.Path}/Notes", "changed")];
        this.deepestPrice = [KVPatchOperation.Set($"{leaf.Path}/Detail/Price", 99m)];
        this.everyLeafCost = DeepFormFiller.SetLeafCosts(shape, 7m).ToArray();
        this.dropFirstSubtree = [KVPatchOperation.Drop("/Components/c1/Detail")];

        // A draft with 100 changed fields spread over the tree, for validate / deltas / commit / rebase.
        this.changedOverlay = KVOverlay.Create(this.committed.Clone(), "alice");
        this.changedRoot = this.model.Bind(this.changedOverlay, null);
        this.changedRoot.Patch(hundredChanges);

        // Upstream changed the same 100 fields differently: 100 value conflicts on rebase.
        var upstreamOverlay = KVOverlay.Create(this.committed.Clone(), "bob");
        var upstreamRoot = this.model.Bind(upstreamOverlay, null);
        upstreamRoot.Patch(SpreadOver(DeepFormFiller.SetAll(shape, variant: 2).ToArray(), ChangedFields));
        this.upstream = this.committed.Clone();
        this.upstreamCommit = upstreamRoot.CreateCommit(DateTimeOffset.UtcNow);
        this.upstream.Apply(this.upstreamCommit);
    }

    [Benchmark(Description = "bind (load → typed root)")]
    public KVRootNode Bind() => this.model.Bind(KVOverlay.Create(this.committed.Clone(), "alice"), null);

    [Benchmark(Description = "project the whole tree to JSON")]
    public int ProjectAll() => AggregateDataProjector.Project(KVOverlay.Create(this.committed, "alice"), this.model.Definition).Count;

    [Benchmark(Description = "build the tree from empty (ADD / INIT / SET)")]
    public KVRootNode BuildTree()
    {
        var root = this.model.BindNew(KVOverlay.Create(this.model.Definition.NewSnapshot(), "alice"));
        root.Patch(this.build);
        return root;
    }

    [Benchmark(Description = "bind + patch 1 deepest field")]
    public KVRootNode PatchDeepestField() => this.BindAndPatch(this.deepestNote);

    [Benchmark(Description = "bind + patch 1 deepest price (rolls up every level)")]
    public KVRootNode PatchDeepestPrice() => this.BindAndPatch(this.deepestPrice);

    [Benchmark(Description = "bind + patch every leaf's unit cost")]
    public KVRootNode PatchEveryLeafCost() => this.BindAndPatch(this.everyLeafCost);

    [Benchmark(Description = "bind + patch every field")]
    public KVRootNode PatchAll() => this.BindAndPatch(this.everyField);

    [Benchmark(Description = "bind + drop the first top-level subtree")]
    public KVRootNode DropSubtree() => this.BindAndPatch(this.dropFirstSubtree);

    [Benchmark(Description = "validate (100 changes)")]
    public int Validate() => this.changedRoot.Validate().Errors.Count;

    [Benchmark(Description = "compute deltas (100 changes)")]
    public int Deltas() => this.changedRoot.GetAllChanges().Changes.Count;

    [Benchmark(Description = "commit (validate + build, 100 changes)")]
    public KVCommit Commit() => this.changedRoot.CreateCommit(DateTimeOffset.UtcNow);

    [Benchmark(Description = "rebase with 100 conflicts")]
    public int Rebase()
    {
        var overlay = KVOverlay.Create(this.committed.Clone(), "alice");
        overlay.Changes = new KVDictionary(this.changedOverlay.Changes);
        overlay.BeginRebase(this.upstream, [this.upstreamCommit]);
        return overlay.Conflicts.Count;
    }

    // Every n-th operation, so the changes reach every level of the tree rather than the first branch.
    private static KVPatchOperation[] SpreadOver(KVPatchOperation[] operations, int count)
    {
        var step = Math.Max(1, operations.Length / count);
        return operations.Where((_, index) => index % step == 0).Take(count).ToArray();
    }

    private KVRootNode BindAndPatch(KVPatchOperation[] operations)
    {
        var root = this.model.Bind(KVOverlay.Create(this.committed.Clone(), "alice"), null);
        root.Patch(operations);
        return root;
    }
}
