using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using x86cc.KVBind.Sample.Api.DeepForms;

namespace x86cc.KVBind.Benchmarks;

/// <summary>
/// A long history over HTTP against the running sample: one depth-4, ~1,000-field component tree (`deep`)
/// is built and filled a few edits per commit, then re-edited until it has <c>commits</c> commits, each
/// one open draft → patch → commit, as a user saving every few edits would. Records how each step's cost
/// grows with the history. Along the way other users open drafts that end up 1, 10, 100 and 1,000 commits
/// behind; each edits fields the later commits change too and adds a component of its own. At the end each
/// is rebased (every real conflict resolved), finished and committed.
///   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- history http://localhost:5101 [commits] [editsPerCommit] [report.md]
/// </summary>
public static class HistoryRun
{
    public static async Task<int> RunAsync(string[] args)
    {
        var baseUrl = args.Length > 1 ? args[1].TrimEnd('/') : "http://localhost:5101";
        var commitCount = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1000;
        var editsPerCommit = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 3;
        var reportPath = args.Length > 4 ? args[4] : "history-report.md";

        var shape = DeepFormShape.AtDepth(4);
        var history = DeepFormHistory.Commits(shape, editsPerCommit).Take(commitCount).ToList();
        var buildCommits = CountBuildCommits(shape, editsPerCommit);
        var behinds = new[] { 1, 10, 100, 1000 }.Where(behind => behind <= commitCount).ToArray();
        var url = $"/api/models/{DeepFormModels.Name}";

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
        var calls = new List<Call>();
        Console.WriteLine($"deep d4: {shape.FieldValues} field values, {shape.Components} components; {commitCount} commits of {editsPerCommit} edits (the tree is complete after {buildCommits})");

        var created = await Send(calls, "create", "-", () => http.PostAsJsonAsync(url, new { user = "writer", values = new Dictionary<string, string>(StringComparer.Ordinal) { ["Title"] = "History" } }));
        var id = created.Body!["id"]!.GetValue<string>();
        var stale = new Dictionary<int, (string User, string DraftUrl, int Edits)>();
        var started = Stopwatch.GetTimestamp();

        for (var index = 0; index < history.Count; index++)
        {
            foreach (var behind in behinds.Where(behind => history.Count - behind == index))
            {
                var user = $"behind-{behind}";
                var edits = DeepFormHistory.StaleDraftEdits(history, index, behind, user).Select(LoadRun.ToRequest).ToArray();
                var opened = await Send(calls, "stale: open draft", "-", () => http.PostAsJsonAsync($"{url}/{id}/drafts", new { user }));
                var draftUrl = $"{url}/{id}/drafts/{opened.Body!["draftId"]!.GetValue<string>()}";
                await Send(calls, "stale: patch", "-", () => http.PostAsJsonAsync($"{draftUrl}/patch", edits));
                stale[behind] = (user, draftUrl, edits.Length);
            }

            var bucket = BucketOf(index + 1);
            var draft = await Send(calls, "open draft", bucket, () => http.PostAsJsonAsync($"{url}/{id}/drafts", new { user = "writer" }));
            var writerUrl = $"{url}/{id}/drafts/{draft.Body!["draftId"]!.GetValue<string>()}";
            var operations = history[index].Select(LoadRun.ToRequest).ToArray();
            await Send(calls, "patch", bucket, () => http.PostAsJsonAsync($"{writerUrl}/patch", operations));
            await Send(calls, "commit", bucket, () => http.PostAsJsonAsync($"{writerUrl}/commit", new { user = "writer" }));

            if ((index + 1) % 100 == 0)
            {
                Console.WriteLine($"  {index + 1} commits, {Stopwatch.GetElapsedTime(started).TotalSeconds:0.0} s");
            }
        }

        for (var i = 0; i < 5; i++)
        {
            await Send(calls, "get snapshot", "end", () => http.GetAsync($"{url}/{id}/snapshot"));
            await Send(calls, "get history: newest 50", "end", () => http.GetAsync($"{url}/{id}/changesets?limit=50"));
            await Send(calls, "get history: 50 from the oldest end", "end", () => http.GetAsync($"{url}/{id}/changesets?offset={Math.Max(0, commitCount - 50)}&limit=50"));
            await Send(calls, "get history: largest page (500)", "end", () => http.GetAsync($"{url}/{id}/changesets?limit=500"));
        }

        var rebases = new List<RebaseResult>();
        foreach (var behind in behinds)
        {
            var (user, draftUrl, edits) = stale[behind];
            var staleCommit = await Send(calls, "stale commit", "-", () => http.PostAsJsonAsync($"{draftUrl}/commit", new { user }), HttpStatusCode.Conflict);
            var begin = await Send(calls, "begin rebase", "-", () => http.PostAsync($"{draftUrl}/rebase", null));
            var conflicts = begin.Body!["conflicts"]!.AsArray().Select(c => c!).ToList();
            var open = conflicts.Where(c => c["requiresResolution"]!.GetValue<bool>() && c["resolution"]!.GetValue<string>() == "Unresolved").ToList();
            var resolves = new List<Call>();
            foreach (var conflict in open)
            {
                var path = conflict["path"]!.GetValue<string>();
                resolves.Add(await Send(calls, "resolve", "-", () => http.PostAsJsonAsync($"{draftUrl}/rebase/resolve", new { path, resolution = "Ours" })));
            }

            var finish = await Send(calls, "finish rebase", "-", () => http.PostAsync($"{draftUrl}/rebase/finish", null));
            var commit = await Send(calls, "commit after rebase", "-", () => http.PostAsJsonAsync($"{draftUrl}/commit", new { user }));
            var real = conflicts.Count(c => c["requiresResolution"]!.GetValue<bool>());
            rebases.Add(new RebaseResult(behind, edits, conflicts.Count, real, real - open.Count, staleCommit, begin, resolves, finish, commit));
            Console.WriteLine($"  behind {behind}: {conflicts.Count} reviewed, {real} conflicts ({real - open.Count} derived, auto-resolved), begin {begin.ClientMs:0.0} ms");
        }

        var snapshot = await Send(calls, "final snapshot", "end", () => http.GetAsync($"{url}/{id}/snapshot"));
        var report = Report(calls, rebases, shape, commitCount, editsPerCommit, buildCommits, baseUrl, id, snapshot);
        await File.WriteAllTextAsync(reportPath, report);
        Console.WriteLine(report);
        Console.WriteLine($"Report written to {Path.GetFullPath(reportPath)}; aggregate {id}");
        return 0;
    }

    private static int CountBuildCommits(DeepFormShape shape, int editsPerCommit)
    {
        var buildOperations = DeepFormFiller.Build(shape).Count();
        var operations = 0;
        var commits = 0;
        foreach (var commit in DeepFormHistory.Commits(shape, editsPerCommit))
        {
            operations += commit.Length;
            commits++;
            if (operations >= buildOperations)
            {
                return commits;
            }
        }

        return commits;
    }

    private static string BucketOf(int commit) => commit switch
    {
        <= 10 => "commits 1–10",
        <= 100 => "commits 11–100",
        <= 500 => "commits 101–500",
        _ => "commits 501–1000+",
    };

    private static async Task<Call> Send(List<Call> calls, string step, string bucket, Func<Task<HttpResponseMessage>> send, HttpStatusCode? expected = null)
    {
        var started = Stopwatch.GetTimestamp();
        using var response = await send();
        var body = await response.Content.ReadAsStringAsync();
        var clientMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var ok = expected is { } status ? response.StatusCode == status : response.IsSuccessStatusCode;
        if (!ok)
        {
            throw new InvalidOperationException($"{step} answered {(int)response.StatusCode}: {body}");
        }

        var (stages, total) = LoadRun.ParseServerTiming(response.Headers.TryGetValues("Server-Timing", out var values) ? string.Join(", ", values) : string.Empty);
        var call = new Call(step, bucket, response.StatusCode, clientMs, total, stages, Encoding.UTF8.GetByteCount(body), body.Length > 0 ? JsonNode.Parse(body) : null);
        calls.Add(call);
        return call;
    }

    private static string Report(
        List<Call> calls,
        List<RebaseResult> rebases,
        DeepFormShape shape,
        int commitCount,
        int editsPerCommit,
        int buildCommits,
        string baseUrl,
        string id,
        Call snapshot)
    {
        var b = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        b.AppendLine("# KVBind sample — a long history on a deep form");
        b.AppendLine();
        b.AppendLine(inv, $"`deep`, depth 4: {shape.FieldValues} field values in {shape.Components} components. {commitCount} commits of {editsPerCommit} edits each (open draft → patch → commit), the tree complete after {buildCommits} commits, then every field re-edited. {baseUrl}, sequential requests, {DateTimeOffset.Now:yyyy-MM-dd HH:mm}. Aggregate `{id}`; final total cost {snapshot.Body!["data"]!["TotalCost"]}.");
        b.AppendLine();
        b.AppendLine("## Each commit, as the history grows (p50 / p95 ms, server stages p50)");
        b.AppendLine();
        b.AppendLine("| Commits | Step | n | client p50 | client p95 | server p50 | stages |");
        b.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var group in calls.Where(c => c.Bucket.StartsWith("commits", StringComparison.Ordinal)).GroupBy(c => (c.Bucket, c.Step)))
        {
            Row(b, group.Key.Bucket, group.Key.Step, [.. group]);
        }

        b.AppendLine();
        b.AppendLine("## Reading the aggregate at the end");
        b.AppendLine();
        b.AppendLine("| | Step | n | client p50 | client p95 | server p50 | stages | response KB |");
        b.AppendLine("|---|---|---:|---:|---:|---:|---|---:|");
        foreach (var group in calls.Where(c => c.Bucket == "end").GroupBy(c => c.Step, StringComparer.Ordinal))
        {
            Row(b, string.Empty, group.Key, [.. group], withSize: true);
        }

        b.AppendLine();
        b.AppendLine("## Rebasing drafts left behind");
        b.AppendLine();
        b.AppendLine("Each draft edited fields that later commits changed too, and added a component of its own. Reviewed = every path in the rebase review list (incoming changes and conflicts); derived conflicts (subtotals, totals) are resolved automatically and recomputed on finish; the rest are resolved Ours, one request each.");
        b.AppendLine();
        b.AppendLine("| Behind by | draft edits | reviewed | conflicts (derived) | begin ms | begin stages | resolve p50 ms | finish ms | commit ms |");
        b.AppendLine("|---:|---:|---:|---:|---:|---|---:|---:|---:|");
        foreach (var r in rebases)
        {
            var resolveP50 = r.Resolves.Count == 0 ? "—" : LoadRun.Percentile(r.Resolves.Select(c => c.ClientMs), 0.5).ToString("0.0", inv);
            b.AppendLine(inv, $"| {r.BehindBy} | {r.DraftEdits} | {r.Reviewed} | {r.Conflicts} ({r.AutoResolved}) | {r.Begin.ClientMs:0.0} | {FormatStages(r.Begin.Stages)} | {resolveP50} | {r.Finish.ClientMs:0.0} | {r.Commit.ClientMs:0.0} |");
        }

        return b.ToString();
    }

    private static void Row(StringBuilder b, string first, string step, List<Call> group, bool withSize = false)
    {
        var inv = CultureInfo.InvariantCulture;
        var stageNames = group.SelectMany(c => c.Stages.Keys).Distinct(StringComparer.Ordinal);
        var stages = string.Join(", ", stageNames.Select(name =>
            $"{name} {LoadRun.Percentile(group.Select(c => c.Stages.GetValueOrDefault(name)), 0.5).ToString("0.##", inv)}"));
        var size = withSize ? string.Create(inv, $" {group.Average(c => c.Bytes) / 1024:0.0} |") : string.Empty;
        b.AppendLine(inv, $"| {first} | {step} | {group.Count} | {LoadRun.Percentile(group.Select(c => c.ClientMs), 0.5):0.0} | {LoadRun.Percentile(group.Select(c => c.ClientMs), 0.95):0.0} | {LoadRun.Percentile(group.Select(c => c.ServerMs), 0.5):0.0} | {stages} |{size}");
    }

    private static string FormatStages(IReadOnlyDictionary<string, double> stages) =>
        string.Join(", ", stages.Select(pair => $"{pair.Key} {pair.Value.ToString("0.##", CultureInfo.InvariantCulture)}"));

    private sealed record Call(string Step, string Bucket, HttpStatusCode Status, double ClientMs, double ServerMs, IReadOnlyDictionary<string, double> Stages, int Bytes, JsonNode? Body);

    private sealed record RebaseResult(int BehindBy, int DraftEdits, int Reviewed, int Conflicts, int AutoResolved, Call Stale, Call Begin, List<Call> Resolves, Call Finish, Call Commit);
}
