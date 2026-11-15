using BenchmarkDotNet.Attributes;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.LargeForms;

namespace x86cc.KVBind.Benchmarks;

// The stages of one API request on the generated large forms (samples/tools/GenerateLargeForms.cs), at
// 100 / 500 / 1000 / 2000 section fields, on a committed aggregate with every field set and 50 lines:
//   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*LargeForm*"
// Each benchmark binds its own overlay, as a request does, except Validate/Deltas/Project which reuse one.
[MemoryDiagnoser]
[ShortRunJob]
public class LargeFormBenchmarks
{
    private const int ChangedFields = 100;
    private const int LineCount = 50;

    private IAggregateModel model = null!;
    private KVSnapshot committed = null!;
    private KVOverlay changedOverlay = null!;
    private KVRootNode changedRoot = null!;
    private KVPatchOperation[] oneChange = null!;
    private KVPatchOperation[] hundredChanges = null!;
    private KVPatchOperation[] everyField = null!;
    private KVSnapshot upstream = null!;
    private KVCommit upstreamCommit = null!;

    [Params(100, 500, 1000, 2000)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.model = SampleSetup.CreateCatalog(out _).Find(LargeFormModels.NameOf(this.Size))!;

        // A committed aggregate with every field set and 50 lines.
        var overlay = KVOverlay.Create(this.model.Definition.NewSnapshot(), "setup");
        var root = this.model.BindNew(overlay);
        root.Patch(LargeFormFiller.FillAll(this.model.Schema.Document).Concat(LargeFormFiller.AddLines(LineCount)));
        this.committed = overlay.Snapshot.Clone();
        this.committed.Apply(root.CreateCommit(DateTimeOffset.UtcNow));

        this.everyField = LargeFormFiller.FillAll(this.model.Schema.Document, variant: 1).ToArray();
        this.oneChange = this.everyField[..1];
        this.hundredChanges = this.everyField[..ChangedFields];

        // A draft with 100 changed fields, for validate / deltas / commit / rebase.
        this.changedOverlay = KVOverlay.Create(this.committed.Clone(), "alice");
        this.changedRoot = this.model.Bind(this.changedOverlay, null);
        this.changedRoot.Patch(this.hundredChanges);

        // Upstream changed the same 100 fields differently: 100 value conflicts on rebase.
        var upstreamOverlay = KVOverlay.Create(this.committed.Clone(), "bob");
        var upstreamRoot = this.model.Bind(upstreamOverlay, null);
        var upstreamChanges = LargeFormFiller.FillAll(this.model.Schema.Document, variant: 2).Take(ChangedFields).ToArray();
        upstreamRoot.Patch(upstreamChanges);
        this.upstream = this.committed.Clone();
        this.upstreamCommit = upstreamRoot.CreateCommit(DateTimeOffset.UtcNow);
        this.upstream.Apply(this.upstreamCommit);
    }

    [Benchmark(Description = "bind (load → typed root)")]
    public KVRootNode Bind() => this.model.Bind(KVOverlay.Create(this.committed.Clone(), "alice"), null);

    [Benchmark(Description = "project all fields to JSON")]
    public int ProjectAll() => AggregateDataProjector.Project(KVOverlay.Create(this.committed, "alice"), this.model.Definition).Count;

    [Benchmark(Description = "bind + patch 1 field")]
    public KVRootNode PatchOne() => this.BindAndPatch(this.oneChange);

    [Benchmark(Description = "bind + patch 100 fields")]
    public KVRootNode PatchHundred() => this.BindAndPatch(this.hundredChanges);

    [Benchmark(Description = "bind + patch every field")]
    public KVRootNode PatchAll() => this.BindAndPatch(this.everyField);

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

    private KVRootNode BindAndPatch(KVPatchOperation[] operations)
    {
        var root = this.model.Bind(KVOverlay.Create(this.committed.Clone(), "alice"), null);
        root.Patch(operations);
        return root;
    }
}
