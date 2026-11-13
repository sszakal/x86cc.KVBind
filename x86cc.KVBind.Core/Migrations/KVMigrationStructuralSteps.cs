using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

internal static class KVMigrationPaths
{
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
        this.Ops.Add(new KVReshapeDrop(relativePath));
        return this;
    }

    /// <summary>Adds / backfills a field unique to the new type.</summary>
    public KVNodeReshapeBuilder Set(string relativePath, Func<KVMigrationFieldView, object?> value, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(value);
        this.Ops.Add(new KVReshapeSet(relativePath, value, overwrite));
        return this;
    }

    /// <summary>
    /// Renames a field, or a whole subtree such as a field group, within the node (carrying its values).
    /// Throws when the destination already holds data; drop it first if it should be replaced.
    /// </summary>
    public KVNodeReshapeBuilder Rename(string fromRelative, string toRelative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromRelative);
        ArgumentException.ThrowIfNullOrWhiteSpace(toRelative);
        this.Ops.Add(new KVReshapeRename(fromRelative, toRelative));
        return this;
    }
}

// Swaps a nested node's $type discriminator to a new token and reshapes its fields. Honours the target's
// $type filter, so only instances currently of the old type are converted (others are left alone).
//
// The reshape ops run in declaration order against a working copy of the node's leaves, so each op sees the
// result of the ones before it (a Set after a Rename sees the renamed value). The delta is the difference
// between the node before and after: one tombstone per removed leaf, one upsert per new or changed leaf.
internal sealed class ReplaceNestedTypeStep(KVTarget target, string toType, IReadOnlyList<KVReshapeOp> ops) : IKVMigrationStep
{
    public void BuildInto(KVDictionary source, KVDictionary delta, KVMigrationContext context)
    {
        foreach (var nodePath in target.Resolve(source))
        {
            delta[nodePath + "/$type"] = toType;
            if (ops.Count == 0)
                continue;

            var before = ReadLeaves(source, nodePath);
            var after = new Dictionary<string, KVValue>(before, StringComparer.Ordinal);
            foreach (var op in ops)
            {
                switch (op)
                {
                    case KVReshapeDrop drop:
                        RemoveSubtree(after, nodePath + "/" + drop.Relative);
                        break;

                    case KVReshapeSet set:
                        ApplySet(source, after, context, nodePath, set);
                        break;

                    case KVReshapeRename rename:
                        ApplyRename(after, nodePath, rename);
                        break;
                }
            }

            WriteDifference(before, after, delta);
        }
    }

    // The node's stored leaves, keyed by absolute path.
    private static Dictionary<string, KVValue> ReadLeaves(KVDictionary source, string nodePath)
    {
        var leaves = new Dictionary<string, KVValue>(StringComparer.Ordinal);
        var prefix = nodePath + "/";
        foreach (var (key, value) in source)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) && value != KVValue.Tombstone)
                leaves[key] = value;
        }

        return leaves;
    }

    private static void RemoveSubtree(Dictionary<string, KVValue> leaves, string path)
    {
        var doomed = new List<string>();
        foreach (var key in leaves.Keys)
        {
            if (IsSameOrUnder(key, path))
                doomed.Add(key);
        }

        foreach (var key in doomed)
            leaves.Remove(key);
    }

    // The factory's view still reads the pre-migration source (siblings, absolute paths); only the
    // presence / overwrite decision uses the working copy.
    private static void ApplySet(KVDictionary source, Dictionary<string, KVValue> leaves, KVMigrationContext context, string nodePath, KVReshapeSet set)
    {
        var setPath = nodePath + "/" + set.Relative;
        if (leaves.ContainsKey(setPath) && !set.Overwrite)
            return;

        var computed = set.Factory(new KVMigrationFieldView(source, setPath, context));
        if (computed is null)
            RemoveSubtree(leaves, setPath);
        else
            leaves[setPath] = KVValue.FromObject(computed);
    }

    // Moves a field or a whole subtree (a field group, a nested collection). Nothing to move is a no-op;
    // a destination that already holds data is an error, since merging two subtrees has no obvious meaning.
    private static void ApplyRename(Dictionary<string, KVValue> leaves, string nodePath, KVReshapeRename rename)
    {
        var from = nodePath + "/" + rename.FromRelative;
        var to = nodePath + "/" + rename.ToRelative;
        if (string.Equals(from, to, StringComparison.Ordinal))
            return;

        var moved = TakeSubtree(leaves, from);
        if (moved.Count == 0)
            return;

        EnsureDestinationFree(leaves, nodePath, rename, to);

        foreach (var leaf in moved)
            leaves[to + leaf.Key[from.Length..]] = leaf.Value;
    }

    // Removes and returns every leaf at or under the path.
    private static List<KeyValuePair<string, KVValue>> TakeSubtree(Dictionary<string, KVValue> leaves, string path)
    {
        var taken = new List<KeyValuePair<string, KVValue>>();
        foreach (var leaf in leaves)
        {
            if (IsSameOrUnder(leaf.Key, path))
                taken.Add(leaf);
        }

        foreach (var leaf in taken)
            leaves.Remove(leaf.Key);

        return taken;
    }

    private static void EnsureDestinationFree(Dictionary<string, KVValue> leaves, string nodePath, KVReshapeRename rename, string to)
    {
        foreach (var key in leaves.Keys)
        {
            if (IsSameOrUnder(key, to))
            {
                throw new InvalidOperationException(
                    $"Cannot rename '{rename.FromRelative}' to '{rename.ToRelative}' under '{nodePath}': the destination already holds data at '{key}'. Drop it first.");
            }
        }
    }

    private static bool IsSameOrUnder(string key, string path) =>
        key.StartsWith(path, StringComparison.Ordinal) && (key.Length == path.Length || key[path.Length] == '/');

    private static void WriteDifference(Dictionary<string, KVValue> before, Dictionary<string, KVValue> after, KVDictionary delta)
    {
        foreach (var key in before.Keys)
        {
            if (!after.ContainsKey(key))
                delta[key] = KVValue.Tombstone;
        }

        foreach (var (key, value) in after)
        {
            if (!before.TryGetValue(key, out var previous) || !previous.Equals(value))
                delta[key] = value;
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
