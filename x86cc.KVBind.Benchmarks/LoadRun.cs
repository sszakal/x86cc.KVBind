using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.DeepForms;
using x86cc.KVBind.Sample.Api.LargeForms;

namespace x86cc.KVBind.Benchmarks;

/// <summary>
/// End-to-end run against the running sample API (Phase 3): for each large-form size and each depth of the
/// recursive tree (`deep`), creates aggregates and drives the draft workflow over HTTP, recording each call's
/// round trip and the server's stage timings (Server-Timing). Writes a Markdown report with p50 / p95 per
/// endpoint and form. The optional filter keeps only forms whose name contains it (e.g. "deep").
///   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- load http://localhost:5101 [rounds] [report.md] [filter]
/// </summary>
public static class LoadRun
{
    public static async Task<int> RunAsync(string[] args)
    {
        var baseUrl = args.Length > 1 ? args[1].TrimEnd('/') : "http://localhost:5101";
        var rounds = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 5;
        var reportPath = args.Length > 3 ? args[3] : "load-report.md";
        var filter = args.Length > 4 ? args[4] : string.Empty;

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(2) };
        var catalog = SampleSetup.CreateCatalog(out _);
        var samples = new List<Sample>();

        foreach (var size in LargeFormModels.Sizes.Where(size => LargeFormModels.NameOf(size).Contains(filter, StringComparison.Ordinal)))
        {
            var form = LargeFormModels.NameOf(size);
            var model = catalog.Find(form)!;
            var url = $"/api/models/{model.Name}";
            var fill = LargeFormFiller.FillAll(model.Schema.Document, variant: 1).Select(ToRequest).ToArray();
            var lines = LargeFormFiller.AddLines(50).Select(ToRequest).ToArray();
            Console.WriteLine($"{model.Name}: {rounds} rounds, {fill.Length} fields, {lines.Length} line operations");

            for (var round = 0; round < rounds; round++)
            {
                var user = $"load-{round}";
                var created = await Call(samples, form, "create", () => http.PostAsJsonAsync(url, new { user, values = new Dictionary<string, string>(StringComparer.Ordinal) { ["S001/F09Text"] = $"Load {round}" } }));
                var id = created["id"]!.GetValue<string>();
                var draft = await Call(samples, form, "open draft", () => http.PostAsJsonAsync($"{url}/{id}/drafts", new { user }));
                var draftUrl = $"{url}/{id}/drafts/{draft["draftId"]!.GetValue<string>()}";

                await Call(samples, form, $"patch all {fill.Length} fields", () => http.PostAsJsonAsync($"{draftUrl}/patch", fill));
                await Call(samples, form, "patch 50 lines (300 ops)", () => http.PostAsJsonAsync($"{draftUrl}/patch", lines));
                for (var i = 0; i < 10; i++)
                {
                    var one = new[] { fill[(i * 37) % fill.Length] with { Value = ToRequest(LargeFormFiller.FillAll(model.Schema.Document, variant: i + 2).ElementAt((i * 37) % fill.Length)).Value } };
                    await Call(samples, form, "patch 1 field", () => http.PostAsJsonAsync($"{draftUrl}/patch", one));
                    await Call(samples, form, "get draft", () => http.GetAsync(draftUrl));
                }

                await Call(samples, form, "validate", () => http.PostAsJsonAsync($"{draftUrl}/validate", new { }));
                await Call(samples, form, "commit", () => http.PostAsJsonAsync($"{draftUrl}/commit", new { user }));
                await Call(samples, form, "get snapshot", () => http.GetAsync($"{url}/{id}/snapshot"));
            }
        }

        foreach (var shape in DeepFormShape.ThousandFields.Where(shape => DeepName(shape).Contains(filter, StringComparison.Ordinal)))
        {
            var form = DeepName(shape);
            var url = $"/api/models/{DeepFormModels.Name}";
            var build = DeepFormFiller.Build(shape).Select(ToRequest).ToArray();
            var leaf = DeepFormFiller.DeepestLeaf(shape);
            var leafCosts = DeepFormFiller.SetLeafCosts(shape, 7m).Select(ToRequest).ToArray();
            Console.WriteLine($"{form}: {rounds} rounds, {shape.FieldValues} field values, {shape.Components} components, {build.Length} operations to build");

            for (var round = 0; round < rounds; round++)
            {
                var user = $"load-{round}";
                var created = await Call(samples, form, "create", () => http.PostAsJsonAsync(url, new { user, values = new Dictionary<string, string>(StringComparer.Ordinal) { ["Title"] = $"Load {round}" } }));
                var id = created["id"]!.GetValue<string>();
                var draft = await Call(samples, form, "open draft", () => http.PostAsJsonAsync($"{url}/{id}/drafts", new { user }));
                var draftUrl = $"{url}/{id}/drafts/{draft["draftId"]!.GetValue<string>()}";

                await Call(samples, form, $"build the tree ({build.Length} ops)", () => http.PostAsJsonAsync($"{draftUrl}/patch", build));
                for (var i = 0; i < 10; i++)
                {
                    var one = new[] { new PatchRequest("SET", $"{leaf.Path}/Detail/Price", 20m + i) };
                    await Call(samples, form, "patch 1 deepest price", () => http.PostAsJsonAsync($"{draftUrl}/patch", one));
                    await Call(samples, form, "get draft", () => http.GetAsync(draftUrl));
                }

                await Call(samples, form, $"patch {leafCosts.Length} leaf costs", () => http.PostAsJsonAsync($"{draftUrl}/patch", leafCosts));
                await Call(samples, form, "validate", () => http.PostAsJsonAsync($"{draftUrl}/validate", new { }));
                await Call(samples, form, "commit", () => http.PostAsJsonAsync($"{draftUrl}/commit", new { user }));
                await Call(samples, form, "get snapshot", () => http.GetAsync($"{url}/{id}/snapshot"));
            }
        }

        var report = Report(samples, rounds, baseUrl);
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"Report written to {Path.GetFullPath(reportPath)}");
        return 0;
    }

    internal static PatchRequest ToRequest(x86cc.KVBind.Core.KVPatchOperation operation) => operation.Value switch
    {
        x86cc.KVBind.Core.KVAddPatchPayload add => new PatchRequest(operation.OperationCode, operation.Path, new { itemId = add.ItemId, typeToken = add.TypeToken }),
        _ => new PatchRequest(operation.OperationCode, operation.Path, operation.Value),
    };

    internal static (Dictionary<string, double> Stages, double Total) ParseServerTiming(string header)
    {
        var stages = new Dictionary<string, double>(StringComparer.Ordinal);
        var total = 0d;
        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(';', StringSplitOptions.TrimEntries);
            var duration = pieces.Skip(1).FirstOrDefault(p => p.StartsWith("dur=", StringComparison.Ordinal));
            if (duration is null)
            {
                continue;
            }

            var ms = double.Parse(duration[4..], CultureInfo.InvariantCulture);
            if (pieces[0] == "total")
            {
                total = ms;
            }
            else
            {
                stages[pieces[0]] = ms;
            }
        }

        return (stages, total);
    }

    internal static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static string DeepName(DeepFormShape shape) => $"deep d{shape.Depth}";

    private static async Task<JsonObject> Call(List<Sample> samples, string form, string endpoint, Func<Task<HttpResponseMessage>> send)
    {
        var started = Stopwatch.GetTimestamp();
        using var response = await send();
        var body = await response.Content.ReadAsStringAsync();
        var clientMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{endpoint} on {form} answered {(int)response.StatusCode}: {body}");
        }

        var (stages, total) = ParseServerTiming(response.Headers.TryGetValues("Server-Timing", out var values) ? string.Join(", ", values) : string.Empty);
        samples.Add(new Sample(form, endpoint, clientMs, total, stages, Encoding.UTF8.GetByteCount(body)));
        return JsonNode.Parse(body) as JsonObject ?? [];
    }

    private static string Report(List<Sample> samples, int rounds, string baseUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# KVBind sample — end-to-end load run");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"{rounds} rounds per form against {baseUrl}, sequential requests, {DateTimeOffset.Now:yyyy-MM-dd HH:mm}. Client = round trip measured by the caller; server = the API's own total (Server-Timing); stages are server medians.");
        builder.AppendLine();
        builder.AppendLine("| Form | Endpoint | n | client p50 ms | client p95 ms | server p50 ms | response KB | server stages (p50 ms) |");
        builder.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
        foreach (var group in samples.GroupBy(s => (s.Form, s.Endpoint)))
        {
            var list = group.ToList();
            var stageNames = list.SelectMany(s => s.Stages.Keys).Distinct(StringComparer.Ordinal);
            var stages = string.Join(", ", stageNames.Select(name =>
                $"{name} {Percentile(list.Select(s => s.Stages.GetValueOrDefault(name)), 0.5).ToString("0.##", CultureInfo.InvariantCulture)}"));
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {group.Key.Form} | {group.Key.Endpoint} | {list.Count} | {Percentile(list.Select(s => s.ClientMs), 0.5):0.0} | {Percentile(list.Select(s => s.ClientMs), 0.95):0.0} | {Percentile(list.Select(s => s.ServerMs), 0.5):0.0} | {list.Average(s => s.ResponseBytes) / 1024:0.0} | {stages} |");
        }

        return builder.ToString();
    }

    internal sealed record Sample(string Form, string Endpoint, double ClientMs, double ServerMs, IReadOnlyDictionary<string, double> Stages, int ResponseBytes);

    internal sealed record PatchRequest(string OperationCode, string Path, object? Value);
}
