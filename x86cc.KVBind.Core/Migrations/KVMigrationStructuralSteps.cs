using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

internal static class KVMigrationPaths
{
    public static bool HasPathOrDescendant(KVDictionary source, string path)
    {
        if (source.ContainsKey(path))
            return true;

        var prefix = path + "/";
        foreach (var key in source.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    public static string Parent(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }
}

// ── Nested-node type replacement (e.g. CAT -> DOG) ────────────────────────────────────────────────

internal abstract record KVReshapeOp;
internal sealed record KVReshapeDrop(string Relative) : KVReshapeOp;
internal sealed record KVReshapeSet(string Relative, Func<KVMigrationFieldView, object?> Factory, bool Overwrite) : KVReshapeOp;
internal sealed record KVReshapeRename(string FromRelative, string ToRelative) : KVReshapeOp;

/// <summary>
/// Reshapes the fields of a nested node when its type changes: drop fields that don't exist on the new type,
/// add (backfill) fields unique to the new type, and rename fields. Fields common to both types are simply
/// left untouched. Paths are relative to the node instance.
/// </summary>
public sealed class KVNodeReshapeBuilder
{
    internal List<KVReshapeOp> Ops { get; } = new();

    /// <summary>Removes a field (or subtree) that the new type does not have.</summary>
    public KVNodeReshapeBuilder Drop(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        Ops.Add(new KVReshapeDrop(relativePath));
        return this;
    }

    /// <summary>Adds / backfills a field unique to the new type.</summary>
    public KVNodeReshapeBuilder Set(string relativePath, Func<KVMigrationFieldView, object?> value, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(value);
        Ops.Add(new KVReshapeSet(relativePath, value, overwrite));
        return this;
    }

    /// <summary>Renames a field within the node (carrying its value).</summary>
    public KVNodeReshapeBuilder Rename(string fromRelative, string toRelative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRelative);
        ArgumentException.ThrowIfNullOrWhiteSpace(toRelative);
        Ops.Add(new KVReshapeRename(fromRelative, toRelative));
        return this;
    }
}

// Swaps a nested node's $type discriminator to a new token and reshapes its fields. Honours the target's
// $type filter, so only instances currently of the old type are converted (others are left alone).
internal sealed class ReplaceNestedTypeStep(KVTarget target, string toType, IReadOnlyList<KVReshapeOp> ops) : IKVMigrationStep
{
    public void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context)
    {
        foreach (var nodePath in target.Resolve(source))
        {
            delta[nodePath + "/$type"] = toType;

            foreach (var op in ops)
            {
                switch (op)
                {
                    case KVReshapeDrop drop:
                        var dropPath = nodePath + "/" + drop.Relative;
                        if (KVMigrationPaths.HasPathOrDescendant(source, dropPath))
                            delta[dropPath] = KVValue.Tombstone;
                        break;

                    case KVReshapeSet set:
                        var setPath = nodePath + "/" + set.Relative;
                        var hasValue = source.TryGetValue(setPath, out var current) && current != KVValue.Tombstone;
                        if (hasValue && !set.Overwrite)
                            break;
                        var computed = set.Factory(new KVMigrationFieldView(source, setPath, context));
                        var value = computed is null ? KVValue.Tombstone : KVValue.FromObject(computed);
                        if (hasValue && current!.Equals(value))
                            break;
                        delta[setPath] = value;
                        break;

                    case KVReshapeRename rename:
                        var from = nodePath + "/" + rename.FromRelative;
                        if (source.TryGetValue(from, out var moved) && moved != KVValue.Tombstone)
                        {
                            delta[nodePath + "/" + rename.ToRelative] = moved!;
                            delta[from] = KVValue.Tombstone;
                        }
                        break;
                }
            }
        }
    }
}

// Renames the last segment of every subtree a target resolves to — wildcard-aware, so it renames a nested
// node (or sub-collection) under collection-item ids that aren't known statically. Carries all descendants.
internal sealed class RenameSubtreeStep(KVTarget target, string toSegment) : IKVMigrationStep
{
    public void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context)
    {
        foreach (var nodePath in target.Resolve(source))
        {
            var parent = KVMigrationPaths.Parent(nodePath);
            var newPath = parent.Length == 0 ? toSegment : parent + "/" + toSegment;
            var prefix = nodePath + "/";

            foreach (var key in source.Keys)
            {
                string? rewritten =
                    string.Equals(key, nodePath, StringComparison.Ordinal) ? newPath
                    : key.StartsWith(prefix, StringComparison.Ordinal) ? newPath + "/" + key[prefix.Length..]
                    : null;

                if (rewritten is null)
                    continue;

                delta[rewritten] = source[key];
                delta[key] = KVValue.Tombstone;
            }
        }
    }
}
