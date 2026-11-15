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

namespace x86cc.KVBind.Benchmarks;

/// <summary>
/// Memory for capacity planning: for completely filled forms of about 1,000 and 2,000 fields, flat and four
/// levels deep, what one loaded aggregate keeps alive, what one request allocates, and how much of an
/// aggregate is path strings (the keys every stored value is filed under).
///   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- capacity [report.md]
/// </summary>
public static class CapacityRun
{
    private const int Copies = 24;
    private const int Requests = 50;

    private static readonly JsonSerializerOptions StorageJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(string[] args)
    {
        var reportPath = args.Length > 1 ? args[1] : "capacity-report.md";
        var catalog = SampleSetup.CreateCatalog(out _);
        var deep = catalog.Find(DeepFormModels.Name)!;
        var rows = new List<Row>();

        foreach (var size in new[] { 1000, 2000 })
        {
            var model = catalog.Find(LargeFormModels.NameOf(size))!;
            var fill = LargeFormFiller.FillAll(model.Schema.Document).Concat(LargeFormFiller.AddLines(50)).ToArray();
            var edit = LargeFormFiller.FillAll(model.Schema.Document, variant: 1).Take(1).ToArray();
            rows.Add(Measure($"flat, {size} fields + 50 lines", model, fill, edit));
        }

        foreach (var shape in new[] { DeepFormShape.AtDepth(4), new DeepFormShape(TopLevel: 6, Branching: 2, Depth: 4) })
        {
            KVPatchOperation[] fill = [KVPatchOperation.Set("/Title", "Capacity"), .. DeepFormFiller.Build(shape)];
            var leaf = DeepFormFiller.DeepestLeaf(shape);
            KVPatchOperation[] edit = [KVPatchOperation.Set($"{leaf.Path}/Detail/Price", 99m)];
            rows.Add(Measure($"depth 4, {shape.FieldValues} values ({shape.Components} components)", deep, fill, edit));
        }

        var report = Report(rows);
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"Report written to {Path.GetFullPath(reportPath)}");
        return 0;
    }

    private static Row Measure(string form, IAggregateModel model, KVPatchOperation[] fill, KVPatchOperation[] edit)
    {
        var overlay = KVOverlay.Create(model.Definition.NewSnapshot(), "setup");
        var root = model.BindNew(overlay);
        root.Patch(fill);
        var committed = overlay.Snapshot.Clone();
        committed.Apply(root.CreateCommit(DateTimeOffset.UtcNow));
        var stored = JsonSerializer.SerializeToUtf8Bytes(committed, StorageJson);

        KVSnapshot Load() => JsonSerializer.Deserialize<KVSnapshot>(stored, StorageJson)!;

        // Kept alive while a request holds the aggregate: loaded, bound, every node visited.
        var held = Retained(() =>
        {
            var bound = model.Bind(KVOverlay.Create(Load(), "alice"), null);
            bound.Validate();
            return bound;
        });

        // Path strings: one per stored key, each a separate instance in every loaded aggregate.
        var keyBytes = Load().Data.Keys.Sum(key => StringBytes(key.Length));
        var distinctSegments = Load().Data.Keys.SelectMany(key => key.Split('/')).Distinct(StringComparer.Ordinal).ToList();
        var segmentBytes = distinctSegments.Sum(segment => StringBytes(segment.Length));

        // One request as the sample serves a draft edit: load, bind, patch one field, build the JSON
        // response, serialize it; everything it allocates, kept or not.
        var allocated = Allocated(() =>
        {
            var request = model.Bind(KVOverlay.Create(Load(), "alice"), null);
            request.Patch(edit);
            var data = AggregateDataProjector.Project(request.Model.Overlay, model.Definition);
            return JsonSerializer.SerializeToUtf8Bytes(data, ResponseJson).Length;
        });

        return new Row(form, committed.Data.Count, stored.Length, held, keyBytes, distinctSegments.Count, segmentBytes, allocated);
    }

    // A .NET string: 22 bytes of header and terminator plus 2 per char, rounded up to 8.
    private static long StringBytes(int length) => (22 + (2L * length) + 7) / 8 * 8;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Critical Code Smell", "S1215:\"GC.Collect\" should not be called", Justification = "A measurement tool: the live heap after a full collection is what is being measured.")]
    private static long Retained<T>(Func<T> create)
        where T : class
    {
        create();
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

    private static long Allocated(Func<int> request)
    {
        request();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Requests; i++)
        {
            request();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Requests;
    }

    private static string Report(List<Row> rows)
    {
        var inv = CultureInfo.InvariantCulture;
        var b = new StringBuilder();
        b.AppendLine("# KVBind — memory for capacity planning");
        b.AppendLine();
        b.AppendLine(inv, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm}, .NET {Environment.Version}. Held = retained while one request holds the aggregate (loaded from its stored JSON, bound, every node visited), averaged over {Copies}. Per request = everything one draft edit allocates (load, bind, patch one field, build and serialize the JSON response), averaged over {Requests}.");
        b.AppendLine();
        b.AppendLine("| Form | stored keys | stored JSON | held in memory | of which path strings | distinct path segments (if shared) | allocated per request | 100 held | 1,000 held |");
        b.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            b.AppendLine(inv, $"| {r.Form} | {r.Keys} | {Kb(r.StoredBytes)} | {Kb(r.Held)} | {Kb(r.KeyBytes)} ({100.0 * r.KeyBytes / r.Held:0}%) | {r.Segments} ({Kb(r.SegmentBytes)}) | {Kb(r.AllocatedPerRequest)} | {Mb(100 * r.Held)} | {Mb(1000 * r.Held)} |");
        }

        return b.ToString();
    }

    private static string Kb(long bytes) => (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB";

    private static string Mb(long bytes) => (bytes / 1048576d).ToString("0", CultureInfo.InvariantCulture) + " MB";

    private sealed record Row(string Form, int Keys, long StoredBytes, long Held, long KeyBytes, int Segments, long SegmentBytes, long AllocatedPerRequest);
}
