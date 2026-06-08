using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Benchmarks;

// Shared builders and field read/write helpers used by every benchmark, so the native and
// KVBind graphs are populated and traversed identically. Each collection has ChildCount
// children at every of the three levels.
public static class GraphFactory
{
    public const int ChildCount = 20;

    private static readonly DateTime BaseDateTime = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly BaseDateOnly = new(2024, 1, 1);

    // ── Native graph ────────────────────────────────────────────────────────────

    public static NativeRoot BuildNative()
    {
        var root = new NativeRoot();
        Fill(root, 0);
        Fill(root.Component, 1);

        for (var i = 0; i < ChildCount; i++)
        {
            var level1 = new NativeComponentCollectionItemLevel1();
            Fill(level1, i);
            for (var j = 0; j < ChildCount; j++)
            {
                var level2 = new NativeComponentCollectionItemLevel2();
                Fill(level2, j);
                for (var k = 0; k < ChildCount; k++)
                {
                    var level3 = new NativeComponentCollectionItemLevel3();
                    Fill(level3, k);
                    level2.Collection.Add(level3);
                }
                level1.Collection.Add(level2);
            }
            root.Collection.Add(level1);
        }

        return root;
    }

    // ── KVBind graph ────────────────────────────────────────────────────────────

    public static KvRoot BuildKvBind()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "bench");
        var root = KVRootNode.Create<KvRoot>(overlay, KvComponentDefinition.Build());

        Fill(root, 0);
        Fill(root.Component, 1);

        for (var i = 0; i < ChildCount; i++)
        {
            var level1 = root.Collection.Create();
            Fill(level1, i);
            for (var j = 0; j < ChildCount; j++)
            {
                var level2 = level1.Collection.Create();
                Fill(level2, j);
                for (var k = 0; k < ChildCount; k++)
                {
                    var level3 = level2.Collection.Create();
                    Fill(level3, k);
                }
            }
        }

        return root;
    }

    // ── Field access ──────────────────────────────────────────────────────────────

    // Writes all 16 fields from a seed. Every value depends on the seed so a re-write with a
    // fresh seed actually changes each field (KVBind skips no-op writes via value equality).
    public static void Fill(IComponent c, int seed)
    {
        c.BooleanField = (seed & 1) == 0;
        c.CharField = (char)('A' + seed % 26);
        c.IntField = seed;
        c.FloatField = seed + 0.5f;
        c.DoubleField = seed + 0.25d;
        c.DecimalField = seed + 0.75m;
        c.StringField = "value-" + seed;
        c.DateTimeField = BaseDateTime.AddSeconds(seed);
        c.DateTimeOffsetField = new DateTimeOffset(BaseDateTime.AddSeconds(seed));
        c.TimeOnlyField = new TimeOnly(seed % 24, seed % 60);
        c.DateOnlyField = BaseDateOnly.AddDays(seed % 1000);
        c.TimespanField = TimeSpan.FromSeconds(seed);
        c.GuidField = new Guid(seed, (short)(seed & 0xFFFF), (short)((seed >> 16) & 0xFFFF), 1, 2, 3, 4, 5, 6, 7, 8);
        c.ArrayOfInts = [seed, seed + 1, seed + 2];
        c.ArrayOfStrings = ["s" + seed, "t" + seed];
        c.ArrayOfDates = [BaseDateTime.AddSeconds(seed)];
    }

    // Reads all 16 fields and folds them into a checksum so the JIT cannot elide the reads.
    public static long ReadAll(IComponent c)
    {
        long sum = 0;
        sum += c.BooleanField ? 1 : 0;
        sum += c.CharField;
        sum += c.IntField;
        sum += (long)c.FloatField;
        sum += (long)c.DoubleField;
        sum += (long)c.DecimalField;
        sum += c.StringField?.Length ?? 0;
        sum += c.DateTimeField.Ticks;
        sum += c.DateTimeOffsetField.Ticks;
        sum += c.TimeOnlyField.Ticks;
        sum += c.DateOnlyField.DayNumber;
        sum += c.TimespanField.Ticks;
        sum += c.GuidField.GetHashCode();
        sum += c.ArrayOfInts?.Length ?? 0;
        sum += c.ArrayOfStrings?.Length ?? 0;
        sum += c.ArrayOfDates?.Length ?? 0;
        return sum;
    }
}
