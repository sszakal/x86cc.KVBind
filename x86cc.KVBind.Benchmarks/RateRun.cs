using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using x86cc.KVBind.Sample.Api.DeepForms;

namespace x86cc.KVBind.Benchmarks;

/// <summary>
/// Steady load at a fixed rate against a running API process, sampling that process's resident memory: how
/// much RAM the API really uses under traffic, garbage collection included. Requests are sent on a fixed
/// schedule (not one after another), as real clients arrive: 60% read a draft, 30% patch one field of it,
/// 10% read the snapshot, spread over several aggregates of the deep form (depth 4, about 2,100 values).
///   dotnet run -c Release --project x86cc.KVBind.Benchmarks -- rate &lt;apiUrl&gt; &lt;apiPid&gt; [requestsPerSecond] [seconds] [label] [report.md]
/// </summary>
public static class RateRun
{
    private const int Aggregates = 100;
    private const int WarmUpSeconds = 10;

    private static DeepFormShape Shape { get; } = new(TopLevel: 6, Branching: 2, Depth: 4);

    private static string Leaf => DeepFormFiller.DeepestLeaf(Shape).Path + "/Detail/Price";

    public static async Task<int> RunAsync(string[] args)
    {
        var baseUrl = args[1].TrimEnd('/');
        var pid = int.Parse(args[2], CultureInfo.InvariantCulture);
        var rate = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 100;
        var seconds = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 60;
        var label = args.Length > 5 ? args[5] : "run";
        var reportPath = args.Length > 6 ? args[6] : null;

        using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 512 }) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        var drafts = await SetUpAsync(http);
        var rssIdle = ReadRss(pid);

        var latencies = new List<double>();
        var failures = 0;
        var statuses = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var samples = new List<(double Seconds, long Rss)>();
        var clock = Stopwatch.StartNew();
        var total = (WarmUpSeconds + seconds) * rate;
        var inFlight = new List<Task>(total);
        using var sampler = new Timer(_ => { lock (samples) samples.Add((clock.Elapsed.TotalSeconds, ReadRss(pid))); }, null, 0, 250);

        for (var i = 0; i < total; i++)
        {
            var due = TimeSpan.FromSeconds((double)i / rate);
            var wait = due - clock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait);
            }

            var index = i;
            var measured = i >= WarmUpSeconds * rate;
            inFlight.Add(Task.Run(async () =>
            {
                var (url, draft) = drafts[index % drafts.Count];
                var started = Stopwatch.GetTimestamp();
                try
                {
                    using var response = (index % 10) switch
                    {
                        < 6 => await http.GetAsync(draft),
                        < 9 => await http.PostAsJsonAsync($"{draft}/patch", new[] { new { operationCode = "SET", path = Leaf, value = (object)(10m + (index % 97)) } }),
                        _ => await http.GetAsync($"{url}/snapshot"),
                    };
                    await response.Content.CopyToAsync(Stream.Null);
                    if (!response.IsSuccessStatusCode)
                    {
                        Interlocked.Increment(ref failures);
                        statuses.AddOrUpdate(((int)response.StatusCode).ToString(CultureInfo.InvariantCulture), 1, (_, n) => n + 1);
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    Interlocked.Increment(ref failures);
                    statuses.AddOrUpdate(exception.GetType().Name, 1, (_, n) => n + 1);
                }

                if (measured)
                {
                    lock (latencies) latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }));
        }

        await Task.WhenAll(inFlight);
        var duration = clock.Elapsed.TotalSeconds;
        await sampler.DisposeAsync();

        List<long> steady;
        lock (samples)
        {
            steady = samples.Where(s => s.Seconds >= WarmUpSeconds).Select(s => s.Rss).ToList();
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"| {label} | {rate}/s | {latencies.Count / Math.Max(1, duration - WarmUpSeconds):0} | {Mb(rssIdle)} | {Mb(Percentile(steady, 0.5))} | {Mb(Percentile(steady, 0.95))} | {Mb(steady.DefaultIfEmpty(0).Max())} | {Percentile(latencies, 0.5):0.0} | {Percentile(latencies, 0.99):0.0} | {failures}{(statuses.IsEmpty ? string.Empty : " (" + string.Join(", ", statuses.Select(pair => $"{pair.Key}: {pair.Value}")) + ")")} |");
        Console.WriteLine(line);
        if (reportPath is not null)
        {
            if (!File.Exists(reportPath))
            {
                await File.WriteAllTextAsync(reportPath, Header());
            }

            await File.AppendAllTextAsync(reportPath, line + Environment.NewLine);
        }

        return failures == 0 ? 0 : 1;
    }

    private static string Header() =>
        new StringBuilder()
            .AppendLine("| Setting | load | done/s | RSS idle | RSS p50 | RSS p95 | RSS max | latency p50 ms | p99 ms | failed |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|")
            .ToString();

    // Aggregates of the deep form with every field filled, each with one open draft that has an edit.
    private static async Task<List<(string Url, string Draft)>> SetUpAsync(HttpClient http)
    {
        var build = DeepFormFiller.Build(Shape).Select(LoadRun.ToRequest).ToArray();
        var url = $"/api/models/{DeepFormModels.Name}";
        var drafts = new List<(string, string)>();
        for (var i = 0; i < Aggregates; i++)
        {
            var created = await Json(await http.PostAsJsonAsync(url, new { user = "rate", values = new Dictionary<string, string>(StringComparer.Ordinal) { ["Title"] = $"Rate {i}" } }));
            var id = created["id"]!.GetValue<string>();
            var setup = await Json(await http.PostAsJsonAsync($"{url}/{id}/drafts", new { user = "setup" }));
            var setupDraft = $"{url}/{id}/drafts/{setup["draftId"]!.GetValue<string>()}";
            (await http.PostAsJsonAsync($"{setupDraft}/patch", build)).EnsureSuccessStatusCode();
            (await http.PostAsJsonAsync($"{setupDraft}/commit", new { user = "setup" })).EnsureSuccessStatusCode();
            var draft = await Json(await http.PostAsJsonAsync($"{url}/{id}/drafts", new { user = "editor" }));
            drafts.Add(($"{url}/{id}", $"{url}/{id}/drafts/{draft["draftId"]!.GetValue<string>()}"));
        }

        return drafts;
    }

    private static async Task<JsonObject> Json(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (JsonNode.Parse(await response.Content.ReadAsStringAsync()) as JsonObject)!;
    }

    // Resident set size of a process, in bytes (what a container's memory limit counts).
    private static long ReadRss(int pid)
    {
        foreach (var line in File.ReadLines($"/proc/{pid}/status"))
        {
            if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
            {
                return long.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) * 1024;
            }
        }

        return 0;
    }

    private static T Percentile<T>(List<T> values, double percentile)
    {
        if (values.Count == 0)
        {
            return default!;
        }

        var sorted = values.Order().ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    private static string Mb(long bytes) => (bytes / 1048576d).ToString("0", CultureInfo.InvariantCulture) + " MB";
}
