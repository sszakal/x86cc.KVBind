using System;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    // Stored key that survives a reset (a $type discriminator); null when the node has none.
    internal virtual string? ResetKeepKey => null;

    // Replaces this node's data with a freshly born object of the same type, in place: everything stored
    // under the node is wiped (its $type discriminator is kept), then its declared defaults are applied,
    // including default nested types and seeded sub-collections. A collection item keeps its id and its
    // position. The root cannot be reset; use Clear() to drop draft changes instead.
    public void Reset()
    {
        this.EnsureBound();
        if (this.Parent is null)
            throw new InvalidOperationException("The root aggregate cannot be reset; use Clear() to drop draft changes.");

        this.Model.Overlay.ResetSubtree(this.Model.DataPath, this.ResetKeepKey);
        this.RebindCurrentContextForPatch();
        this.ApplyDefaultsRecursive();
    }
}
