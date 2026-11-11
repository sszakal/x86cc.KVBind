using System;

namespace x86cc.KVBind.Core;

public abstract partial class KVNode
{
    /// <summary>
    /// Records a domain event on the root aggregate this node belongs to — call it from domain methods and
    /// change reactions (on the root, a field group, a collection item or a nested node) to state what
    /// happened in business terms. Events are held in memory on the bound root, in the order raised; the
    /// application reads them with <see cref="KVRootNode.DequeueEvents"/> and publishes them. They are not
    /// part of the overlay or the commit.
    /// </summary>
    protected void Raise(object domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        this.EnsureBound();
        KVChangeReactionRuntime.ResolveRoot(this).AddPendingEvent(domainEvent);
    }
}
