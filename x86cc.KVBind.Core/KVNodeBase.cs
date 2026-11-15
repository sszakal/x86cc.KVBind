namespace x86cc.KVBind.Core;

// Common base for KVNode and KVCollectionNodeBase — replaces the empty IKVNode marker interface.
// An abstract class, not an interface: its member is internal, so code outside KVBind cannot implement it.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1694:An abstract class should have both abstract and concrete methods", Justification = "Public type with an internal abstract member; an interface would let other assemblies implement it.")]
public abstract class KVNodeBase
{
    internal abstract string GetCanonicalPath();
}
