using BenchmarkDotNet.Running;
using x86cc.KVBind.Benchmarks;
using x86cc.KVBind.Benchmarks.Prototype;

// Spike: `dotnet run -c Release -- mem` runs the retained-memory comparison instead of the benchmarks.
if (args is ["mem"])
{
    MemoryComparison.Run();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// Anchors the top-level statements to a named type for BenchmarkSwitcher.FromAssembly.
public partial class Program;
