using BenchmarkDotNet.Attributes;

namespace x86cc.KVBind.Benchmarks;

// 1. Initialize: build the full 3-level graph (ChildCount children per collection) from scratch.
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
    private NativeRoot native = null!;
    private KvRoot kvBind = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.native = GraphFactory.BuildNative();
        this.kvBind = GraphFactory.BuildKvBind();
    }

    [Benchmark(Baseline = true)]
    public long Native()
    {
        long sum = 0;
        foreach (var component in this.native.GetAllComponents())
            sum += GraphFactory.ReadAll(component);
        return sum;
    }

    [Benchmark]
    public long KvBind()
    {
        long sum = 0;
        foreach (var component in this.kvBind.GetAllComponents())
            sum += GraphFactory.ReadAll(component);
        return sum;
    }
}

// 3. Set: GetAllComponents() on the root, then write every field of every component.
[MemoryDiagnoser]
public class SetBenchmark
{
    private NativeRoot native = null!;
    private KvRoot kvBind = null!;
    private int seed;

    [GlobalSetup]
    public void Setup()
    {
        this.native = GraphFactory.BuildNative();
        this.kvBind = GraphFactory.BuildKvBind();
    }

    [Benchmark(Baseline = true)]
    public void Native()
    {
        var nextSeed = ++this.seed;
        foreach (var component in this.native.GetAllComponents())
            GraphFactory.Fill(component, nextSeed);
    }

    [Benchmark]
    public void KvBind()
    {
        var nextSeed = ++this.seed;
        foreach (var component in this.kvBind.GetAllComponents())
            GraphFactory.Fill(component, nextSeed);
    }
}
