using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Benchmarks.Prototype;

// Composite key that shares its parent prefix by reference: sibling keys point at the same parent chain,
// so each prefix node is stored once (one segment string + the node) while the flat dictionary keeps O(1)
// exact lookups. The hash is precomputed; equality short-circuits on reference-equal parents.
public sealed class KVPathKey : IEquatable<KVPathKey>
{
    public KVPathKey? Parent { get; }
    public string Segment { get; }
    private readonly int _hash;

    private KVPathKey(KVPathKey? parent, string segment)
    {
        Parent = parent;
        Segment = segment;
        _hash = HashCode.Combine(parent?._hash ?? 0, StringComparer.Ordinal.GetHashCode(segment));
    }

    public bool Equals(KVPathKey? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || _hash != other._hash) return false;
        return string.Equals(Segment, other.Segment, StringComparison.Ordinal)
               && (ReferenceEquals(Parent, other.Parent) || Equals(Parent, other.Parent));
    }

    public override bool Equals(object? obj) => Equals(obj as KVPathKey);
    public override int GetHashCode() => _hash;

    public string ToPath()
    {
        if (Parent is null) return Segment;
        var stack = new Stack<string>();
        for (var node = this; node is not null; node = node.Parent) stack.Push(node.Segment);
        return string.Join('/', stack);
    }

    // Interns prefix nodes so shared prefixes collapse to one KVPathKey instance.
    public sealed class Builder
    {
        private readonly Dictionary<(KVPathKey?, string), KVPathKey> _interned = new();

        public KVPathKey Build(string path)
        {
            KVPathKey? node = null;
            foreach (var segment in path.Split('/'))
            {
                var slot = (node, segment);
                if (!_interned.TryGetValue(slot, out var next))
                    _interned[slot] = next = new KVPathKey(node, segment);
                node = next;
            }
            return node!;
        }
    }
}

// Thin store wrapper: a flat Dictionary<KVPathKey, KVValue> populated through the interning builder, so
// leaf reads stay O(1) while prefixes are shared. Enumerable → serializable to the flat shape.
public sealed class KVPathStore
{
    private readonly KVPathKey.Builder _builder = new();
    private readonly Dictionary<KVPathKey, KVValue> _map = new();

    public void Set(string path, KVValue value) => _map[_builder.Build(path)] = value;

    public IEnumerable<(string Path, KVValue Value)> Enumerate()
    {
        foreach (var (key, value) in _map)
            yield return (key.ToPath(), value);
    }
}
