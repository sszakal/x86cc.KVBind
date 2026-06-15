using System;

namespace x86cc.KVBind.Core.Model;

internal static class KVPath
{
    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Trim('/');
    }

    public static string NormalizeRelative(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.TrimStart('/');
    }

    public static string Combine(string basePath, string segment)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return segment;
        }

        if (string.IsNullOrWhiteSpace(segment))
        {
            return basePath;
        }

        return basePath + "/" + segment;
    }

    public static bool IsSameOrDescendant(string path, string ancestorPath)
    {
        if (string.IsNullOrWhiteSpace(ancestorPath))
        {
            return true;
        }

        return string.Equals(path, ancestorPath, StringComparison.Ordinal)
               || path.StartsWith(ancestorPath + "/", StringComparison.Ordinal);
    }

    // Span overload for the allocation-free read path (inheritance coverage check). Equivalent to the
    // string version: path equals the ancestor or sits directly under "ancestor/".
    public static bool IsSameOrDescendant(ReadOnlySpan<char> path, string ancestorPath)
    {
        if (string.IsNullOrWhiteSpace(ancestorPath))
        {
            return true;
        }

        var ancestor = ancestorPath.AsSpan();
        if (path.Equals(ancestor, StringComparison.Ordinal))
        {
            return true;
        }

        return path.Length > ancestor.Length
               && path[ancestor.Length] == '/'
               && path[..ancestor.Length].Equals(ancestor, StringComparison.Ordinal);
    }

    public static string? RelativeTo(string path, string parentPath)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return path;
        }

        if (string.Equals(path, parentPath, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var prefix = parentPath + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return path[prefix.Length..];
    }

    public static string ParentPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    public static bool TryGetDirectSegment(string path, string parentPath, Func<string, bool>? excludeSegment, out string segment)
    {
        segment = string.Empty;
        var relative = RelativeTo(path, parentPath);
        if (relative is null || relative.Length == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parentPath))
        {
            if (excludeSegment is not null && excludeSegment(relative))
            {
                return false;
            }

            var firstSlash = relative.IndexOf('/');
            var firstSegment = firstSlash < 0 ? relative : relative[..firstSlash];
            if (excludeSegment is not null && excludeSegment(firstSegment))
            {
                return false;
            }

            segment = relative;
            return true;
        }

        if (relative.Contains('/'))
        {
            return false;
        }

        if (excludeSegment is not null && excludeSegment(relative))
        {
            return false;
        }

        segment = relative;
        return true;
    }
}
