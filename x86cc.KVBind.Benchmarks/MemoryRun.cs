using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.DeepForms;
using x86cc.KVBind.Sample.Api.LargeForms;
using x86cc.KVBind.Sample.Api.Persistence;

namespace x86cc.KVBind.Benchmarks;

/// <summary>
/// How much memory an aggregate holds while a request works on it, and how big it is when stored, for every
/// large form and every depth of the recursive tree. Retained sizes are measured on the live heap (after a
/// full collection, averaged over several copies), each from a freshly deserialized snapshot, as a request
/// that loaded it from storage would hold it; BenchmarkDotNet's "Allocated" column covers what a stage
/// allocates in passing.
///   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- memory [report.md]
/// </summary>
public static class MemoryRun
{
    private const int Copies = 24;
    private const int DraftChanges = 100;

    // As the sample stores documents (Marten: System.Text.Json, camelCase, enums as names).
    private static readonly JsonSerializerOptions StorageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(string[] args)
    {
        var reportPath = args.Length > 1 ? args[1] : "memory-report.md";
        var catalog = SampleSetup.CreateCatalog(out _);
        var rows = new List<Row>();

        foreach (var size in LargeFormModels.Sizes)
        {
            var model = catalog.Find(LargeFormModels.NameOf(size))!;
            var fill = LargeFormFiller.FillAll(model.Schema.Document).Concat(LargeFormFiller.AddLines(50)).ToArray();
            var edits = LargeFormFiller.FillAll(model.Schema.Document, variant: 1).Take(DraftChanges).ToArray();
            rows.Add(Measure(model.Name, model, fill, edits));
        }

        foreach (var shape in DeepFormShape.ThousandFields)
        {
            var model = catalog.Find(DeepFormModels.Name)!;
            KVPatchOperation[] fill = [KVPatchOperation.Set("/Title", "Memory"), .. DeepFormFiller.Build(shape)];
            var all = DeepFormFiller.SetAll(shape, variant: 1).ToArray();
            var step = Math.Max(1, all.Length / DraftChanges);
            var edits = all.Where((_, index) => index % step == 0).Take(DraftChanges).ToArray();
            rows.Add(Measure($"deep d{shape.Depth}", model, fill, edits));
        }

        var report = Report(rows);
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"Report written to {Path.GetFullPath(reportPath)}");
        return 0;
    }

    private static Row Measure(string form, IAggregateModel model, KVPatchOperation[] fill, KVPatchOperation[] edits)
    {
        // The committed aggregate, as JSON: what the store holds and every measurement below loads from.
        var overlay = KVOverlay.Create(model.Definition.NewSnapshot(), "setup");
        var root = model.BindNew(overlay);
        root.Patch(fill);
        var committed = overlay.Snapshot.Clone();
        committed.Apply(root.CreateCommit(DateTimeOffset.UtcNow));
        var snapshotJson = JsonSerializer.SerializeToUtf8Bytes(committed, StorageJson);

        // A draft with 100 changes: as stored now (changes only), and as it was stored with its base inside.
        var draftOverlay = KVOverlay.Create(committed.Clone(), "alice");
        model.Bind(draftOverlay, null).Patch(edits);
        var draft = AggregateDraftDocument.Create(model.Name, Guid.NewGuid(), "alice", draftOverlay);
        var draftJson = JsonSerializer.SerializeToUtf8Bytes(draft, StorageJson);
        draft.Snapshot = committed;
        var draftWithBaseJson = JsonSerializer.SerializeToUtf8Bytes(draft, StorageJson);

        KVSnapshot Load() => JsonSerializer.Deserialize<KVSnapshot>(snapshotJson, StorageJson)!;

        var loaded = Retained(Load);
        var bound = Retained(() => model.Bind(KVOverlay.Create(Load(), "alice"), null));
        var walked = Retained(() =>
        {
            var bind = model.Bind(KVOverlay.Create(Load(), "alice"), null);
            bind.Validate(); // visits every node, so all of them are materialized
            return bind;
        });
        var withDraft = Retained(() =>
        {
            var bind = model.Bind(KVOverlay.Create(Load(), "alice"), null);
            bind.Patch(edits);
            return bind;
        });
        var projected = Retained(() => AggregateDataProjector.Project(KVOverlay.Create(committed, "alice"), model.Definition));

        return new Row(
            form,
            committed.Data.Keys.Count(key => !key.Contains('$', StringComparison.Ordinal)),
            committed.Data.Count,
            loaded,
            bound - loaded,
            walked - loaded,
            withDraft - bound,
            projected,
            snapshotJson.Length,
            draftJson.Length,
            draftWithBaseJson.Length);
    }

    // Bytes one instance keeps alive: the growth of the live heap over several instances, after full
    // collections, divided by their number.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S1215:\"GC.Collect\" should not be called", Justification = "A measurement tool: the live heap after a full collection is what is being measured.")]
    private static long Retained<T>(Func<T> create)
        where T : class
    {
        create(); // warm up: type loading, caches, JIT
        var keep = new T[Copies];
        var before = GC.GetTotalMemory(forceFullCollection: true);
        for (var i = 0; i < Copies; i++)
        {
            keep[i] = create();
        }

        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(keep);
        return Math.Max(0, after - before) / Copies;
    }

    private static string Report(List<Row> rows)
    {
        var inv = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine("# KVBind — memory per aggregate");
        b.AppendLine();
        b.AppendLine(inv, $"Retained = bytes kept alive, measured on the live heap after full collections and averaged over {Copies} instances, each loaded from its stored JSON. {DateTimeOffset.Now:yyyy-MM-dd HH:mm}, .NET {Environment.Version}.");
        b.AppendLine();
        b.AppendLine("## In memory (KB retained)");
        b.AppendLine();
        b.AppendLine("| Form | field values | stored keys | loaded snapshot | + bound root | + every node visited | + after patching 100 fields | JSON tree for the client | bytes per field value (loaded) |");
        b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            b.AppendLine(inv, $"| {r.Form} | {r.FieldValues} | {r.Keys} | {Kb(r.Loaded)} | {Kb(r.Bound)} | {Kb(r.Walked)} | {Kb(r.Draft)} | {Kb(r.Projected)} | {r.Loaded / Math.Max(1, r.FieldValues)} |");
        }

        b.AppendLine();
        b.AppendLine("## Stored (KB of JSON)");
        b.AppendLine();
        b.AppendLine("| Form | snapshot document | draft document, 100 changes | the same draft with its base embedded (before) |");
        b.AppendLine("|---|---:|---:|---:|");
        foreach (var r in rows)
        {
            b.AppendLine(inv, $"| {r.Form} | {Kb(r.SnapshotJson)} | {Kb(r.DraftJson)} | {Kb(r.DraftWithBaseJson)} |");
        }

        return b.ToString();
    }

    private static string Kb(long bytes) => (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture);

    private sealed record Row(
        string Form,
        int FieldValues,
        int Keys,
        long Loaded,
        long Bound,
        long Walked,
        long Draft,
        long Projected,
        long SnapshotJson,
        long DraftJson,
        long DraftWithBaseJson);
}
