using System;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.Core.Model;

public class KVModelRoot : KVModel
{
    public KVModelRoot()
    {
    }

    public KVModelRoot(KVOverlay overlay, KVNodeDefinition definition)
        : base(overlay, string.Empty)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public static KVModelRoot Create(KVOverlay overlay, KVNodeDefinition definition) => new(overlay, definition);

    public KVNodeDefinition? Definition { get; private set; }

    public KVSnapshot Snapshot => Overlay.Snapshot;

    public KVChangeDeltaGroup ComputeDeltas()
    {
        return ComputeNodeDeltas(string.Empty, isCollectionItem: false);
    }

    internal void AttachDefinition(KVNodeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Definition is not null && !ReferenceEquals(Definition, definition))
        {
            throw new InvalidOperationException("KVModelRoot is already attached to a different definition.");
        }

        Definition = definition;
    }

    public void ClearDraft()
    {
        Overlay.Clear();
        ClearRemovedChildMarks(string.Empty);
        PruneDraftChildren(string.Empty);
    }

    public void DiscardDraftPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalizedPath = KVPath.Normalize(path);
        Overlay.Discard(normalizedPath);
        ClearRemovedChildMarks(normalizedPath);
        PruneDraftChildren(normalizedPath);
    }

    public KVCommit CreateCommit(DateTimeOffset timestamp)
    {
        return Overlay.ToCommit(timestamp);
    }

    public string Id { get; set; } = string.Empty;
    
    public string Version { get; set; } = string.Empty;
}
