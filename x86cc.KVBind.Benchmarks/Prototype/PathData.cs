using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Benchmarks.Prototype;

// Generates authentic-shaped path→value entries: a 3-level collection tree keyed by GUIDs, 16 fields per
// leaf item (mirroring IComponent), plus reserved $items/$type leaves. The long shared GUID prefixes are
// what a prefix-sharing structure can dedup, so the shape matters for the memory comparison.
public static class PathData
{
    private static readonly string[] FieldNames =
    [
        "BooleanField", "CharField", "IntField", "FloatField", "DoubleField", "DecimalField",
        "StringField", "DateTimeField", "DateTimeOffsetField", "TimeOnlyField", "DateOnlyField",
        "TimespanField", "GuidField", "ArrayOfInts", "ArrayOfStrings", "ArrayOfDates",
    ];

    // fanout^3 leaf items × (16 fields + $type) + $items arrays. fanout 4 ≈ 1.1k keys, 5 ≈ 2.3k.
    public static (string Path, KVValue Value)[] Generate(int fanout)
    {
        var entries = new List<(string, KVValue)>();
        var seed = 0;

        var level1Ids = NewIds(fanout);
        entries.Add(($"Collection/$items", Ids(level1Ids)));

        foreach (var g1 in level1Ids)
        {
            var p1 = $"Collection/{g1}";
            var level2Ids = NewIds(fanout);
            entries.Add(($"{p1}/Collection/$items", Ids(level2Ids)));

            foreach (var g2 in level2Ids)
            {
                var p2 = $"{p1}/Collection/{g2}";
                var level3Ids = NewIds(fanout);
                entries.Add(($"{p2}/Collection/$items", Ids(level3Ids)));

                foreach (var g3 in level3Ids)
                {
                    var item = $"{p2}/Collection/{g3}";
                    entries.Add(($"{item}/$type", KVValue.FromObject("KvLevel3")));
                    foreach (var (field, value) in ItemValues(seed++))
                        entries.Add(($"{item}/{field}", value));
                }
            }
        }

        return [.. entries];
    }

    private static string[] NewIds(int count)
    {
        var ids = new string[count];
        for (var i = 0; i < count; i++) ids[i] = Guid.NewGuid().ToString("D");
        return ids;
    }

    private static KVValue Ids(string[] ids) => KVValue.FromObject(ids);

    private static IEnumerable<(string Field, KVValue Value)> ItemValues(int seed)
    {
        var values = new object[]
        {
            (seed & 1) == 0,                                            // BooleanField
            (char)('A' + seed % 26),                                    // CharField
            seed,                                                       // IntField
            seed + 0.5f,                                                // FloatField
            seed + 0.25d,                                               // DoubleField
            seed + 0.75m,                                               // DecimalField
            "value-" + seed,                                            // StringField
            new DateTime(2024, 1, 1).AddSeconds(seed),                  // DateTimeField
            new DateTimeOffset(new DateTime(2024, 1, 1).AddSeconds(seed)), // DateTimeOffsetField
            new TimeOnly(seed % 24, seed % 60),                         // TimeOnlyField
            new DateOnly(2024, 1, 1).AddDays(seed % 1000),              // DateOnlyField
            TimeSpan.FromSeconds(seed),                                 // TimespanField
            Guid.NewGuid(),                                             // GuidField
            new[] { seed, seed + 1, seed + 2 },                         // ArrayOfInts
            new[] { "s" + seed, "t" + seed },                           // ArrayOfStrings
            new[] { new DateTime(2024, 1, 1).AddSeconds(seed) },        // ArrayOfDates
        };

        for (var i = 0; i < FieldNames.Length; i++)
            yield return (FieldNames[i], KVValue.FromObject(values[i]));
    }
}
