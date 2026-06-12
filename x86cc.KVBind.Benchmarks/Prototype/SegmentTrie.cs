using System.Text;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Benchmarks.Prototype;

// Store-viable segment trie: keys are split on '/', each segment is one node. Shared path prefixes map to
// the same nodes, so a segment string is stored once regardless of how many leaves share it — the full
// path strings are never retained. Supports exact get, subtree remove, and leaf enumeration (so it can
// serialize back to the flat {path: value} shape).
public sealed class SegmentTrie
{
    private sealed class Node
    {
        public Dictionary<string, Node>? Children;
        public KVValue? Value;
        public bool HasValue;
    }

    private readonly Node _root = new();

    public void Set(string path, KVValue value)
    {
        var node = _root;
        foreach (var segment in path.Split('/'))
        {
            node.Children ??= new(StringComparer.Ordinal);
            if (!node.Children.TryGetValue(segment, out var child))
                node.Children[segment] = child = new Node();
            node = child;
        }
        node.Value = value;
        node.HasValue = true;
    }

    public bool TryGet(string path, out KVValue? value)
    {
        var node = _root;
        foreach (var segment in path.Split('/'))
        {
            if (node.Children is null || !node.Children.TryGetValue(segment, out node!))
            {
                value = null;
                return false;
            }
        }
        value = node.Value;
        return node.HasValue;
    }

    // Removes the path and its whole subtree (the prefix-tombstone equivalent).
    public bool RemoveSubtree(string path)
    {
        var segments = path.Split('/');
        var node = _root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (node.Children is null || !node.Children.TryGetValue(segments[i], out node!))
                return false;
        }
        return node.Children?.Remove(segments[^1]) ?? false;
    }

    public IEnumerable<(string Path, KVValue Value)> EnumerateLeaves()
    {
        var sb = new StringBuilder();
        return Walk(_root, sb);

        static IEnumerable<(string, KVValue)> Walk(Node node, StringBuilder path)
        {
            if (node.HasValue)
                yield return (path.ToString(), node.Value!);
            if (node.Children is null) yield break;
            foreach (var (segment, child) in node.Children)
            {
                var mark = path.Length;
                if (path.Length > 0) path.Append('/');
                path.Append(segment);
                foreach (var leaf in Walk(child, path)) yield return leaf;
                path.Length = mark;
            }
        }
    }
}
