using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVDraftChanges(IReadOnlyList<KVChangeDelta> changes)
{
    public IReadOnlyList<KVChangeDelta> Changes { get; } = changes;
}
