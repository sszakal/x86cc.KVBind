using BenchmarkDotNet.Running;
using x86cc.KVBind.Benchmarks;

// The first argument "load" runs the end-to-end load run against the sample API (LoadRun.cs), "history" the
// long-history run (HistoryRun.cs), "memory" the memory measurements (MemoryRun.cs); anything else goes to
// BenchmarkDotNet.
if (args.Length > 0 && args[0] == "load")
{
    return await LoadRun.RunAsync(args);
}

if (args.Length > 0 && args[0] == "history")
{
    return await HistoryRun.RunAsync(args);
}

if (args.Length > 0 && args[0] == "rate")
{
    return await RateRun.RunAsync(args);
}

if (args.Length > 0 && args[0] == "capacity")
{
    return await CapacityRun.RunAsync(args);
}

if (args.Length > 0 && args[0] == "memory")
{
    return await MemoryRun.RunAsync(args);
}

BenchmarkSwitcher.FromAssembly(typeof(LargeFormBenchmarks).Assembly).Run(args);
return 0;
