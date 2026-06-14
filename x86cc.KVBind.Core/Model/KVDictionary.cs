using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core.Model;

// The flat path-keyed store. Always Ordinal-comparered by construction: the hot read paths probe via
// Dictionary.GetAlternateLookup<ReadOnlySpan<char>>(), which requires an Ordinal comparer, and a bare
// Dictionary rebuilt by a deserializer would otherwise come back with the default comparer (the comparer
// is not part of the serialized state). Baking the comparer into the type makes it survive serialization
// round-trips without any normalization — every stored path→value map is this type, never a plain
// Dictionary<string, KVValue>.
public sealed class KVDictionary : Dictionary<string, KVValue>
{
    public KVDictionary() : base(StringComparer.Ordinal) { }

    public KVDictionary(IEnumerable<KeyValuePair<string, KVValue>> source) : base(source, StringComparer.Ordinal) { }
}
