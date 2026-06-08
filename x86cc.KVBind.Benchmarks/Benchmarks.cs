using BenchmarkDotNet.Attributes;

namespace x86cc.KVBind.Benchmarks;

// 1. Initialise: build the full 3-level graph (ChildCount children per collection) from scratch.
[MemoryDiagnoser]
public class InitializeBenchmark
{
    [Benchmark(Baseline = true)]
    public NativeRoot Native() => GraphFactory.BuildNative();

    [Benchmark]
    public KvRoot KvBind() => GraphFactory.BuildKvBind();
}

// 2. Get: GetAllComponents() on the root, then read every field of every component.
[MemoryDiagnoser]
public class GetBenchmark
{
    private NativeRoot _native = null!;
    private KvRoot _kvBind = null!;

    [GlobalSetup]
    public void Setup()
    {
        _native = GraphFactory.BuildNative();
        _kvBind = GraphFactory.BuildKvBind();
    }

    [Benchmark(Baseline = true)]
    public long Native()
    {
        long sum = 0;
        foreach (var component in _native.GetAllComponents())
            sum += GraphFactory.ReadAll(component);
        return sum;
    }

    [Benchmark]
    public long KvBind()
    {
        long sum = 0;
        foreach (var component in _kvBind.GetAllComponents())
            sum += GraphFactory.ReadAll(component);
        return sum;
    }
}

// 3. Set: GetAllComponents() on the root, then write every field of every component.
[MemoryDiagnoser]
public class SetBenchmark
{
    private NativeRoot _native = null!;
    private KvRoot _kvBind = null!;
    private int _seed;

    [GlobalSetup]
    public void Setup()
    {
        _native = GraphFactory.BuildNative();
        _kvBind = GraphFactory.BuildKvBind();
    }

    [Benchmark(Baseline = true)]
    public void Native()
    {
        var seed = ++_seed;
        foreach (var component in _native.GetAllComponents())
            GraphFactory.Fill(component, seed);
    }

    [Benchmark]
    public void KvBind()
    {
        var seed = ++_seed;
        foreach (var component in _kvBind.GetAllComponents())
            GraphFactory.Fill(component, seed);
    }
}
