using System;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

/// <summary>
/// One declarative operation in a migration. A step reads the (working) source data and writes upserts /
/// tombstones into the migration commit's delta. Steps never mutate the source — the migrator applies the
/// finished commit so later steps and later migrations see the result.
/// </summary>
public interface IKVMigrationStep
{
    void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context);
}

/// <summary>Read view over the source rooted at a target field — lets a backfill read sibling fields.</summary>
public sealed class KVMigrationFieldView
{
    private readonly KVDictionary _source;
    private readonly string _parent;

    internal KVMigrationFieldView(KVDictionary source, string path, KVMigrationContext context)
    {
        _source = source;
        Path = path;
        Context = context;
        var slash = path.LastIndexOf('/');
        _parent = slash < 0 ? string.Empty : path[..slash];
    }

    /// <summary>The absolute path of the field being backfilled.</summary>
    public string Path { get; }

    /// <summary>The batch prepare context (identity + prepared data); empty on the synchronous path.</summary>
    public KVMigrationContext Context { get; }

    /// <summary>True when the field already holds a (non-deleted) value.</summary>
    public bool Exists => _source.TryGetValue(Path, out var value) && value != KVValue.Tombstone;

    /// <summary>Reads a sibling field's value (relative to this field's parent), or null if absent.</summary>
    public object? Sibling(string segment)
    {
        var path = _parent.Length == 0 ? segment : _parent + "/" + segment;
        return _source.TryGetValue(path, out var value) ? value?.Value : null;
    }

    /// <summary>Reads any value by absolute path, or null if absent.</summary>
    public object? Absolute(string path)
        => _source.TryGetValue(path, out var value) ? value?.Value : null;
}

// Sets a value at every matched path. By default only fills absent fields (backfill); overwrite=true also
// replaces existing values. A computed value identical to what's already stored is skipped — the commit
// still bumps the version (see KVCommit.MigrationToVersion), so an all-no-op backfill yields an empty delta.
internal sealed class BackfillStep(KVTarget target, Func<KVMigrationFieldView, object?> factory, bool overwrite)
    : IKVMigrationStep
{
    public void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context)
    {
        foreach (var path in target.Resolve(source))
        {
            var hasValue = source.TryGetValue(path, out var current) && current != KVValue.Tombstone;
            if (hasValue && !overwrite)
                continue;

            var computed = factory(new KVMigrationFieldView(source, path, context));
            var value = computed is null ? KVValue.Tombstone : KVValue.FromObject(computed);

            if (hasValue && current!.Equals(value))
                continue; // no actual change

            delta[path] = value;
        }
    }
}

// Tombstones every matched path (prefix-tombstone removes descendants on Apply). Only emits where the
// source actually has something at or under the path.
internal sealed class RemoveStep(KVTarget target) : IKVMigrationStep
{
    public void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context)
    {
        foreach (var path in target.Resolve(source))
        {
            if (HasPathOrDescendant(source, path))
                delta[path] = KVValue.Tombstone;
        }
    }

    private static bool HasPathOrDescendant(KVDictionary source, string path)
    {
        if (source.ContainsKey(path))
            return true;

        var prefix = path + "/";
        foreach (var key in source.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }
}

// Leaf rename: moves a field's value to a sibling segment under the same parent (tombstoning the old key).
// Honours the target's $type filter, so only the intended subtype's field is renamed.
internal sealed class RenameFieldStep(KVTarget target, string toSegment) : IKVMigrationStep
{
    public void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context)
    {
        foreach (var path in target.Resolve(source))
        {
            if (!source.TryGetValue(path, out var value) || value == KVValue.Tombstone)
                continue;

            var slash = path.LastIndexOf('/');
            var newPath = slash < 0 ? toSegment : path[..(slash + 1)] + toSegment;
            delta[newPath] = value!;
            delta[path] = KVValue.Tombstone;
        }
    }
}

// Subtree rename: rewrites every key under a path prefix to a new prefix — the group-key / collection-key /
// nested-node-key rename. Data-driven over the source's actual keys, so collection item ids are carried.
internal sealed class RenameSegmentStep(string fromPath, string toPath) : IKVMigrationStep
{
    public void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context)
    {
        var fromPrefix = fromPath + "/";
        foreach (var key in source.Keys)
        {
            string? newKey =
                string.Equals(key, fromPath, StringComparison.Ordinal) ? toPath
                : key.StartsWith(fromPrefix, StringComparison.Ordinal) ? toPath + "/" + key[fromPrefix.Length..]
                : null;

            if (newKey is null)
                continue;

            delta[newKey] = source[key];
            delta[key] = KVValue.Tombstone;
        }
    }
}
