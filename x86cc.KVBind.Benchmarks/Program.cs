using BenchmarkDotNet.Running;
using x86cc.KVBind.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// Anchors the top-level statements to a named type for BenchmarkSwitcher.FromAssembly.
public partial class Program;
