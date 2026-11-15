using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace x86cc.KVBind.Sample.Api.Diagnostics;

public static class RequestTimingsExtensions
{
    /// <summary>Writes the request's stage timings (plus the total) as a <c>Server-Timing</c> response header.</summary>
    public static IApplicationBuilder UseServerTiming(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var started = Stopwatch.GetTimestamp();
            context.Response.OnStarting(() =>
            {
                var timings = context.RequestServices.GetService<RequestTimings>();
                var total = Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
                var stages = timings?.ToHeaderValue();
                context.Response.Headers["Server-Timing"] = string.IsNullOrEmpty(stages)
                    ? $"total;dur={total}"
                    : $"{stages}, total;dur={total}";
                return Task.CompletedTask;
            });

            await next(context);
        });
}

/// <summary>
/// Measures the stages of one request (load, bind, patch, validate, project, persist, ...). Each stage is
/// recorded twice: as an OpenTelemetry span (an <see cref="Activity"/> from <see cref="Source"/>, shown in
/// the Aspire dashboard's traces), and as an entry of the response's <c>Server-Timing</c> header, which
/// browsers show in the network panel and the sample UI reads for its performance panel. A stage measured
/// more than once in a request (e.g. two binds) is summed.
/// </summary>
public sealed class RequestTimings
{
    public const string SourceName = "x86cc.KVBind.Sample.Api"; // the app name, which ServiceDefaults already traces

    public static readonly ActivitySource Source = new(SourceName);

    private readonly List<(string Name, double Milliseconds)> stages = [];
    private readonly Lock gate = new();

    public IReadOnlyList<(string Name, double Milliseconds)> Stages
    {
        get
        {
            lock (this.gate)
            {
                return this.stages.ToArray();
            }
        }
    }

    public Stage Measure(string name) => new(this, name, Source.StartActivity($"kvbind.{name}"));

    public T Measure<T>(string name, Func<T> work)
    {
        using var stage = this.Measure(name);
        return work();
    }

    public void Measure(string name, Action work)
    {
        using var stage = this.Measure(name);
        work();
    }

    public async Task<T> MeasureAsync<T>(string name, Func<Task<T>> work)
    {
        using var stage = this.Measure(name);
        return await work();
    }

    public async Task MeasureAsync(string name, Func<Task> work)
    {
        using var stage = this.Measure(name);
        await work();
    }

    /// <summary>The <c>Server-Timing</c> header value, e.g. <c>load;dur=1.20, bind;dur=0.31</c>.</summary>
    public string ToHeaderValue()
    {
        var builder = new StringBuilder();
        foreach (var (name, milliseconds) in this.Stages)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(name).Append(";dur=").Append(milliseconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private void Record(string name, double milliseconds)
    {
        lock (this.gate)
        {
            var index = this.stages.FindIndex(stage => stage.Name == name);
            if (index >= 0)
            {
                this.stages[index] = (name, this.stages[index].Milliseconds + milliseconds);
            }
            else
            {
                this.stages.Add((name, milliseconds));
            }
        }
    }

    public readonly struct Stage(RequestTimings owner, string name, Activity? activity) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();

        public void Dispose()
        {
            owner.Record(name, Stopwatch.GetElapsedTime(this.started).TotalMilliseconds);
            activity?.Dispose();
        }
    }
}
