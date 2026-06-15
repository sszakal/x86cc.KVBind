using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

/// <summary>
/// A structural selector over the flat, path-keyed store. Resolves against the source data to a set of
/// concrete paths, expanding collection-item ids as wildcards and (optionally) filtering polymorphic
/// item / nested-node instances by their stored <c>$type</c> discriminator.
/// </summary>
/// <remarks>
/// Deliberately not regex: the <c>$type</c> filter requires a sibling lookup (<c>{instance}/$type</c>),
/// which can't be expressed as a single-key pattern, and structural matching stays schema-shaped and
/// refactor-checkable rather than relying on string conventions.
/// </remarks>
public sealed class KVTarget
{
    private readonly List<Segment> _segments = new();

    private KVTarget() { }

    /// <summary>Starts a selector at the document root.</summary>
    public static KVTarget Root => new();

    /// <summary>A fixed segment: field group, collection name, nested-node name, or leaf field.</summary>
    public KVTarget Seg(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _segments.Add(new Segment(name, IsWildcard: false, RequireType: null));
        return this;
    }

    /// <summary>
    /// A collection-item position — matches any item id. When <paramref name="ofType"/> is set, only items
    /// whose <c>$type</c> equals that token match (polymorphic collections).
    /// </summary>
    public KVTarget AnyItem(string? ofType = null)
    {
        _segments.Add(new Segment(Name: null, IsWildcard: true, RequireType: ofType));
        return this;
    }

    /// <summary>
    /// Constrains the instance at the path so far to carry <c>$type == typeToken</c> — used to rename a
    /// field on only one subtype of a polymorphic nested node when the field name is shared.
    /// </summary>
    public KVTarget OfType(string typeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeToken);
        if (_segments.Count == 0)
            throw new InvalidOperationException("OfType must follow a segment.");
        _segments[^1] = _segments[^1] with { RequireType = typeToken };
        return this;
    }

    // Resolves to every concrete path the selector matches against the given data.
    internal IEnumerable<string> Resolve(KVDictionary source)
    {
        IEnumerable<string> prefixes = new[] { string.Empty };
        foreach (var segment in _segments)
            prefixes = Expand(prefixes, segment, source);
        return prefixes;
    }

    private static IEnumerable<string> Expand(IEnumerable<string> prefixes, Segment segment, KVDictionary source)
    {
        foreach (var prefix in prefixes)
        {
            if (segment.IsWildcard)
            {
                foreach (var child in ChildSegments(prefix, source))
                {
                    var path = Combine(prefix, child);
                    if (segment.RequireType is null || TypeMatches(path, segment.RequireType, source))
                        yield return path;
                }
            }
            else
            {
                var path = Combine(prefix, segment.Name!);
                if (segment.RequireType is null || TypeMatches(path, segment.RequireType, source))
                    yield return path;
            }
        }
    }

    private static bool TypeMatches(string instancePath, string token, KVDictionary source)
        => source.TryGetValue(Combine(instancePath, "$type"), out var value)
           && value?.Value as string == token;

    // The distinct direct child segments of a prefix present in the data (e.g. the item ids of a collection).
    private static IEnumerable<string> ChildSegments(string prefix, KVDictionary source)
    {
        var scan = prefix.Length == 0 ? string.Empty : prefix + "/";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in source.Keys)
        {
            if (scan.Length > 0 && !key.StartsWith(scan, StringComparison.Ordinal))
                continue;

            var rest = key.AsSpan(scan.Length);
            var slash = rest.IndexOf('/');
            var child = (slash < 0 ? rest : rest[..slash]).ToString();
            if (child.Length > 0 && seen.Add(child))
                yield return child;
        }
    }

    private static string Combine(string a, string b) => a.Length == 0 ? b : a + "/" + b;

    private readonly record struct Segment(string? Name, bool IsWildcard, string? RequireType);
}
