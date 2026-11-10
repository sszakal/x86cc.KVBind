namespace x86cc.KVBind.Core;

// What kind of change a change reaction is being notified about (KVChangeContext.Kind).
public enum KVChangeKind
{
    // A field value was set or unset. OldValue/NewValue are the stored values.
    Value,

    // A collection item was created (NewValue = the item), or a nested node slot was initialized
    // (NewValue = the active node, OldValue = the type token it replaced, or null when the slot was empty).
    Added,

    // A collection item was removed (OldValue = its id), or a nested node slot was dropped
    // (OldValue = the dropped type token).
    Removed,

    // A collection item was reordered. OldValue/NewValue are its old and new index.
    Moved,

    // The data at ChangedPath was replaced wholesale (Reset, Discard, Clear, or an explicit
    // KVRootNode.NotifyReplaced after a rebase). Raised once for every reaction watching that path or
    // anything beneath it; OldValue/NewValue are null, so recompute from the current state.
    Replaced
}
