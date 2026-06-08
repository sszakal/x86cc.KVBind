using System;
using System.Buffers;

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
        ArgumentNullException.ThrowIfNull(segment);

        // Assemble DataPath + "/" + segment into a stack buffer and probe via the span overload, so a read
        // does not allocate the combined path string (the dictionary key) on every field access.
        var seg = segment.AsSpan().Trim('/');
        KVValue? value;
        if (DataPath.Length == 0)
        {
            if (!Overlay.TryGet(seg, out value) || value?.Value is null)
            {
                return default!;
            }
        }
        else
        {
            var length = DataPath.Length + 1 + seg.Length;
            char[]? rented = length > StackPathThreshold ? ArrayPool<char>.Shared.Rent(length) : null;
            Span<char> buffer = rented ?? stackalloc char[StackPathThreshold];
            DataPath.AsSpan().CopyTo(buffer);
            buffer[DataPath.Length] = '/';
            seg.CopyTo(buffer[(DataPath.Length + 1)..]);

            var found = Overlay.TryGet(buffer[..length], out value);
            if (rented is not null)
            {
                ArrayPool<char>.Shared.Return(rented);
            }

            if (!found || value?.Value is null)
            {
                return default!;
            }
        }

        return value.Value is TValue typed
            ? typed
            : throw new InvalidCastException($"Stored value '{ResolveDataPath(segment)}' is '{value.Value.GetType().FullName}', not '{typeof(TValue).FullName}'.");
    }

    // Paths longer than this fall back to a pooled buffer; shorter ones use the stack.
    private const int StackPathThreshold = 512;

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
