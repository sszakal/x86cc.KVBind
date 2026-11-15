namespace x86cc.KVBind.Core;

public sealed class KVChangeDelta(string path, KVChangeDeltaType changeType)
{
    public string Path { get; } = path;

    public KVChangeDeltaType ChangeType { get; } = changeType;

    /// <summary>For a field changed by a patch (<see cref="KVPatchResult.Changes"/>): the value before the
    /// patch; null otherwise, and in <see cref="KVRootNode.GetAllChanges"/>.</summary>
    public object? OldValue { get; init; }

    /// <summary>For a field changed by a patch (<see cref="KVPatchResult.Changes"/>): the value after the
    /// patch; null otherwise, and in <see cref="KVRootNode.GetAllChanges"/>.</summary>
    public object? NewValue { get; init; }
}
