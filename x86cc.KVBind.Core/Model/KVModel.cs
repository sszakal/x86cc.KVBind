using System;

namespace x86cc.KVBind.Core.Model;

// Shared mutable overlay reference — all KVModel instances created from the same root
// share one KVOverlayRef, so ReplaceOverlay is O(1) with no tree traversal.
internal sealed class KVOverlayRef
{
    public KVOverlay Value { get; set; } = null!;

    public static KVOverlayRef From(KVOverlay overlay) => new() { Value = overlay };
}

public class KVModel
{
    private readonly KVOverlayRef _overlayRef;

    public KVOverlay Overlay => _overlayRef.Value;

    internal string DataPath { get; }

    public KVModel()
        : this(KVOverlayRef.From(KVOverlay.Create(new KVSnapshot(), "system")), string.Empty)
    {
    }

    public KVModel(KVOverlay overlay)
        : this(KVOverlayRef.From(overlay), string.Empty)
    {
    }

    internal KVModel(KVOverlayRef overlayRef, string dataPath)
    {
        _overlayRef = overlayRef;
        DataPath = KVPath.Normalize(dataPath);
    }

    internal KVModel CreateChildModel(string childSegment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childSegment);
        return new KVModel(_overlayRef, KVPath.Combine(DataPath, childSegment));
    }

    public TValue Get<TValue>(string segment)
    {
        if (!Overlay.TryGet(ResolveDataPath(segment), out var value) || value?.Value is null)
        {
            return default!;
        }

        return value.Value is TValue typed
            ? typed
            : throw new InvalidCastException($"Stored value '{ResolveDataPath(segment)}' is '{value.Value.GetType().FullName}', not '{typeof(TValue).FullName}'.");
    }

    internal bool TryGetValue(string segment, out KVValue? value)
    {
        return Overlay.TryGet(ResolveDataPath(segment), out value);
    }

    public void Set<TValue>(string segment, TValue value)
    {
        Overlay.Set(ResolveDataPath(segment), new KVValue<TValue>(value));
    }

    internal void SetValue(string segment, KVValue value)
    {
        Overlay.Set(ResolveDataPath(segment), value);
    }

    public bool Remove(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return Overlay.Remove(ResolveDataPath(segment));
    }

    public void ReplaceOverlay(KVOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlayRef.Value = overlay;
    }


    private string ResolveDataPath(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return KVPath.Combine(DataPath, KVPath.Normalize(segment));
    }
}
