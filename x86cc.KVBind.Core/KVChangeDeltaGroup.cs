using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVChangeDeltaGroup(IReadOnlyList<KVChangeDelta> deltas, IReadOnlyList<KVChangeDeltaGroup> children)
{
    public IReadOnlyList<KVChangeDelta> Deltas { get; } = deltas ?? throw new ArgumentNullException(nameof(deltas));

    public IReadOnlyList<KVChangeDeltaGroup> Children { get; } = children ?? throw new ArgumentNullException(nameof(children));

    public IReadOnlyList<KVChangeDelta> Flatten()
    {
        var values = new List<KVChangeDelta>();
        this.Collect(values);
        values.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return values;
    }

    private void Collect(List<KVChangeDelta> values)
    {
        values.AddRange(this.Deltas);
        foreach (var child in this.Children)
        {
            child.Collect(values);
        }
    }
}
