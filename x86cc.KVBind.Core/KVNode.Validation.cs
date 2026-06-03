using System;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core;

// Path resolution helpers used by the validation runtime.
public abstract partial class KVNode
{
    internal string ResolveStoragePath(string fieldKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldKey);
        return KVPath.NormalizeRelative(fieldKey);
    }

    internal string ResolveStoragePathForCanonicalPath(string canonicalPath, string currentCanonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        var normalizedPath = KVPath.Normalize(canonicalPath);
        var normalizedCurrentPath = KVPath.Normalize(currentCanonicalPath);

        if (string.IsNullOrWhiteSpace(normalizedPath)) return string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCurrentPath)) return normalizedPath;
        if (string.Equals(normalizedPath, normalizedCurrentPath, StringComparison.Ordinal)) return string.Empty;
        if (KVPath.IsSameOrDescendant(normalizedPath, normalizedCurrentPath))
            return normalizedPath[(normalizedCurrentPath.Length + 1)..];
        return normalizedPath;
    }
}
