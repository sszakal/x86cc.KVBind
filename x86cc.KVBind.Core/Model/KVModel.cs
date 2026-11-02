using System;
using System.Buffers;

namespace x86cc.KVBind.Core.Model;

public class KVModel
{
    // Paths longer than this fall back to a pooled buffer; shorter ones use the stack.
    private const int StackPathThreshold = 512;

    private readonly KVOverlayRef overlayRef;

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
        this.overlayRef = overlayRef;
        this.DataPath = KVPath.Normalize(dataPath);
    }

    public KVOverlay Overlay => this.overlayRef.Value;

    internal string DataPath { get; }

    public TValue Get<TValue>(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        // Assemble DataPath + "/" + segment into a stack buffer and probe via the span overload, so a read
        // does not allocate the combined path string (the dictionary key) on every field access.
        var seg = segment.AsSpan().Trim('/');
        KVValue? value;
        if (this.DataPath.Length == 0)
        {
            if (!this.Overlay.TryGet(seg, out value) || value?.Value is null)
            {
                return default!;
            }
        }
        else
        {
            var length = this.DataPath.Length + 1 + seg.Length;
            char[]? rented = length > StackPathThreshold ? ArrayPool<char>.Shared.Rent(length) : null;
            Span<char> buffer = rented ?? stackalloc char[StackPathThreshold];
            this.DataPath.AsSpan().CopyTo(buffer);
            buffer[this.DataPath.Length] = '/';
            seg.CopyTo(buffer[(this.DataPath.Length + 1)..]);

            var found = this.Overlay.TryGet(buffer[..length], out value);
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
            : throw new InvalidCastException($"Stored value '{this.ResolveDataPath(segment)}' is '{value.Value.GetType().FullName}', not '{typeof(TValue).FullName}'.");
    }

    public void Set<TValue>(string segment, TValue value)
    {
        this.Overlay.Set(this.ResolveDataPath(segment), new KVValue<TValue>(value));
    }

    public bool Remove(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return this.Overlay.Remove(this.ResolveDataPath(segment));
    }

    public void ReplaceOverlay(KVOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        this.overlayRef.Value = overlay;
    }

    internal KVModel CreateChildModel(string childSegment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childSegment);
        return new KVModel(this.overlayRef, KVPath.Combine(this.DataPath, childSegment));
    }

    internal bool TryGetValue(string segment, out KVValue? value)
    {
        return this.Overlay.TryGet(this.ResolveDataPath(segment), out value);
    }

    internal void SetValue(string segment, KVValue value)
    {
        this.Overlay.Set(this.ResolveDataPath(segment), value);
    }

    private string ResolveDataPath(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return KVPath.Combine(this.DataPath, KVPath.Normalize(segment));
    }
}

// Shared mutable overlay reference — all KVModel instances created from the same root
// share one KVOverlayRef, so ReplaceOverlay is O(1) with no tree traversal.
internal sealed class KVOverlayRef
{
    public KVOverlay Value { get; set; } = null!;

    public static KVOverlayRef From(KVOverlay overlay) => new() { Value = overlay };
}
