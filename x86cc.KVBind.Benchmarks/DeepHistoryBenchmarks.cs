using BenchmarkDotNet.Attributes;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.DeepForms;

namespace x86cc.KVBind.Benchmarks;

// Rebasing on the deep form (depth 4, ~1,000 fields) after a long history: a draft branched off the complete
// tree, edited 20 fields that the next commits change too and added a component of its own; meanwhile
// `Behind` commits of 3 edits each landed (DeepFormHistory). Measures what the API does on rebase:
//   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- --filter "*DeepHistory*"
[MemoryDiagnoser]
[ShortRunJob]
public class DeepHistoryBenchmarks
{
    private const int EditsPerCommit = 3;

    private IAggregateModel model = null!;
    private KVSnapshot baseSnapshot = null!;
    private KVSnapshot latest = null!;
    private KVCommit[] commits = null!;
    private KVDictionary draftChanges = null!;

    [Params(1, 10, 100, 1000)]
    public int Behind { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.model = SampleSetup.CreateCatalog(out _).Find(DeepFormModels.Name)!;
        var shape = DeepFormShape.AtDepth(4);

        // The complete tree, committed once.
        KVPatchOperation[] build = [KVPatchOperation.Set("/Title", "History"), .. DeepFormFiller.Build(shape)];
        var overlay = KVOverlay.Create(this.model.Definition.NewSnapshot(), "setup");
        var root = this.model.BindNew(overlay);
        root.Patch(build);
        this.baseSnapshot = overlay.Snapshot.Clone();
        this.baseSnapshot.Apply(root.CreateCommit(DateTimeOffset.UtcNow));

        // `Behind` upstream commits, each bound, patched and committed on the latest snapshot.
        var upstream = DeepFormHistory.Commits(shape, EditsPerCommit, includeBuild: false).Take(this.Behind).ToList();
        this.latest = this.baseSnapshot.Clone();
        var made = new List<KVCommit>();
        foreach (var operations in upstream)
        {
            var writer = this.model.Bind(KVOverlay.Create(this.latest.Clone(), "writer"), null);
            writer.Patch(operations);
            var commit = writer.CreateCommit(DateTimeOffset.UtcNow);
            this.latest.Apply(commit);
            made.Add(commit);
        }

        this.commits = [.. made];

        // The draft: branched off the base, conflicting with the commits that followed.
        var draft = KVOverlay.Create(this.baseSnapshot.Clone(), "alice");
        var draftRoot = this.model.Bind(draft, null);
        draftRoot.Patch(DeepFormHistory.StaleDraftEdits([build, .. upstream], openedAt: 1, behindBy: this.Behind, user: "alice"));
        this.draftChanges = draft.Changes;
    }

    [Benchmark(Description = "begin rebase (fold the commits, find conflicts)")]
    public int BeginRebase()
    {
        var overlay = this.DraftOverlay();
        overlay.BeginRebase(this.latest, this.commits);
        return overlay.Conflicts.Count;
    }

    [Benchmark(Description = "rebase end to end: begin, resolve, finish, recompute, commit")]
    public KVCommit RebaseAndCommit()
    {
        var overlay = this.DraftOverlay();
        overlay.BeginRebase(this.latest, this.commits);
        foreach (var conflict in overlay.Conflicts.Where(c => c.RequiresResolution).ToArray())
        {
            overlay.ResolveConflict(conflict.Path, IsDerived(conflict.Path) ? KVConflictResolution.Theirs : KVConflictResolution.Ours);
        }

        overlay.FinishRebase();
        var root = this.model.Bind(overlay, null);
        root.NotifyReplaced();
        return root.CreateCommit(DateTimeOffset.UtcNow);
    }

    [Benchmark(Description = "replay the commits onto the base snapshot")]
    public KVSnapshot Replay()
    {
        var snapshot = this.baseSnapshot.Clone();
        foreach (var commit in this.commits)
        {
            snapshot.Apply(commit);
        }

        return snapshot;
    }

    private static bool IsDerived(string path) =>
        path is "TotalCost" or "ComponentCount" || path.EndsWith("/Subtotal", StringComparison.Ordinal);

    private KVOverlay DraftOverlay()
    {
        var overlay = KVOverlay.Create(this.baseSnapshot.Clone(), "alice");
        overlay.Changes = new KVDictionary(this.draftChanges);
        return overlay;
    }
}
