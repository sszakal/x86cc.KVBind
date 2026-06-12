using System.Text.Json;
using Gma.DataStructures.StringSearch;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Benchmarks.Prototype;

// Spike entry point: compares retained bytes of the current string-keyed dictionary against a vendored
// TrieNet PatriciaTrie (memory yardstick) and the two store-viable candidates (segment trie, composite
// KVPath key), and proves the store-viable structures serialize to the identical flat {path: value} JSON.
public static class MemoryComparison
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int Copies = 5;

    public static void Run()
    {
        int[] fanouts = [4, 5];

        Console.WriteLine("== Flat-shape proof (store-viable structures enumerate to identical JSON) ==");
        foreach (var fanout in fanouts)
            ShapeProof(fanout);
        Console.WriteLine();

        Console.WriteLine("== Retained memory (approx, bytes/instance) ==");
        Console.WriteLine($"{"Structure",-22}{"Keys",8}{"Retained KB",14}{"vs dict",10}");

        foreach (var fanout in fanouts)
        {
            var keys = PathData.Generate(fanout).Length;

            var dict = RetainedMemory.BytesPerInstance(Copies, () => BuildDict(fanout));
            var trie = RetainedMemory.BytesPerInstance(Copies, () => BuildSegmentTrie(fanout));
            var comp = RetainedMemory.BytesPerInstance(Copies, () => BuildComposite(fanout));
            // PatriciaTrie interns full keys (never GC'd), so measure it last with a single copy and treat
            // the number as an upper-bound, polluted reference — not directly comparable.
            var patricia = RetainedMemory.BytesPerInstance(1, () => BuildPatricia(fanout));

            Console.WriteLine($"-- fanout {fanout} --");
            Row("Dictionary<string>", keys, dict, dict);
            Row("SegmentTrie", keys, trie, dict);
            Row("Composite KVPath", keys, comp, dict);
            Row("TrieNet Patricia*", keys, patricia, dict);
        }

        Console.WriteLine();
        Console.WriteLine("* TrieNet PatriciaTrie interns every full key string permanently (StringPartition.string.Intern),");
        Console.WriteLine("  so for unique long paths it does NOT save memory and its figure is inflated/non-comparable.");
    }

    private static void Row(string name, int keys, long bytes, long dictBytes)
    {
        var ratio = dictBytes == 0 ? 1.0 : (double)bytes / dictBytes;
        Console.WriteLine($"{name,-22}{keys,8}{bytes / 1024.0,14:N1}{ratio,9:0.00}x");
    }

    // ── builders (each generates its own fresh dataset; the entries array is local and collected) ──

    private static object BuildDict(int fanout)
    {
        var d = new Dictionary<string, KVValue>(StringComparer.Ordinal);
        foreach (var (path, value) in PathData.Generate(fanout)) d[path] = value;
        return d;
    }

    private static object BuildSegmentTrie(int fanout)
    {
        var t = new SegmentTrie();
        foreach (var (path, value) in PathData.Generate(fanout)) t.Set(path, value);
        return t;
    }

    private static object BuildComposite(int fanout)
    {
        var s = new KVPathStore();
        foreach (var (path, value) in PathData.Generate(fanout)) s.Set(path, value);
        return s;
    }

    private static object BuildPatricia(int fanout)
    {
        var t = new PatriciaTrie<KVValue>();
        foreach (var (path, value) in PathData.Generate(fanout)) t.Add(path, value);
        return t;
    }

    // ── flat-shape proof ──

    private static void ShapeProof(int fanout)
    {
        var data = PathData.Generate(fanout);

        var dict = new Dictionary<string, KVValue>(StringComparer.Ordinal);
        var trie = new SegmentTrie();
        var comp = new KVPathStore();
        foreach (var (path, value) in data)
        {
            dict[path] = value;
            trie.Set(path, value);
            comp.Set(path, value);
        }

        var dictJson = FlatJson(dict.Select(kv => (kv.Key, kv.Value)));
        var trieJson = FlatJson(trie.EnumerateLeaves());
        var compJson = FlatJson(comp.Enumerate());

        var identical = dictJson == trieJson && trieJson == compJson;

        // round-trip: parse the dict JSON, rebuild a fresh trie, re-emit, compare.
        var parsed = JsonSerializer.Deserialize<Dictionary<string, KVValue>>(dictJson, JsonOptions)!;
        var roundTrip = new SegmentTrie();
        foreach (var (path, value) in parsed) roundTrip.Set(path, value);
        var roundTripOk = FlatJson(roundTrip.EnumerateLeaves()) == dictJson;

        Console.WriteLine($"  fanout {fanout}: {data.Length} keys, identical JSON across structures = {identical}, round-trip = {roundTripOk}, {dictJson.Length / 1024.0:N1} KB serialized");
    }

    private static string FlatJson(IEnumerable<(string Path, KVValue Value)> entries)
    {
        var sorted = new SortedDictionary<string, KVValue>(StringComparer.Ordinal);
        foreach (var (path, value) in entries) sorted[path] = value;
        return JsonSerializer.Serialize(sorted, JsonOptions);
    }
}
