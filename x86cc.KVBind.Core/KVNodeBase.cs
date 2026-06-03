namespace x86cc.KVBind.Core;

// Common base for KVNode and KVCollectionNodeBase — replaces the empty IKVNode marker interface.
public abstract class KVNodeBase
{
    internal abstract string GetCanonicalPath();
}
