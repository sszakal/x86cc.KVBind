namespace x86cc.KVBind.Core;

public sealed class KVChangeDelta(string path, KVChangeDeltaType changeType)
{
    public string Path { get; } = path;

    public KVChangeDeltaType ChangeType { get; } = changeType;
}