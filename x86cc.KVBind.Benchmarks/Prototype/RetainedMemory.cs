namespace x86cc.KVBind.Benchmarks.Prototype;

// Approximate retained-heap measurement: force a full GC, snapshot total memory, build `copies` instances
// (each from its own fresh dataset, so only what the structure itself retains survives), GC again, and
// take the delta. Coarse by nature (we care about 1x vs 2x vs 5x, not exact bytes).
public static class RetainedMemory
{
    public static long BytesPerInstance(int copies, Func<object> build)
    {
        Quiesce();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var roots = new object[copies];
        for (var i = 0; i < copies; i++) roots[i] = build();

        Quiesce();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(roots);

        return (after - before) / copies;
    }

    private static void Quiesce()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
