using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace x86cc.KVBind.Core.Migrations;

/// <summary>
/// A single schema migration: an ordered set of declarative steps that move data from the previous version
/// to <see cref="ToVersion"/>. Each migration translates into exactly one migration commit (see
/// <see cref="KVMigrator"/>).
/// </summary>
public sealed class KVMigration
{
    public required int ToVersion { get; init; }

    public required IReadOnlyList<IKVMigrationStep> Steps { get; init; }

    /// <summary>
    /// Optional async batch phase. Runs once per migration over the subset of a batch that still needs it,
    /// returning data (typically a lookup keyed by subject id) that backfill factories read via
    /// <see cref="KVMigrationFieldView.Context"/>. Null for purely structural / self-contained migrations.
    /// Only honoured by the batch path (<c>KVMigrator.MigrateBatchAsync</c>).
    /// </summary>
    public Func<IReadOnlyList<KVMigrationSubject>, CancellationToken, Task<object?>>? Prepare { get; init; }

    public static KVMigration Define(int toVersion, Action<KVMigrationBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (toVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(toVersion), "Migration target version must be positive.");

        var builder = new KVMigrationBuilder();
        build(builder);
        return new KVMigration { ToVersion = toVersion, Steps = builder.Steps, Prepare = builder.PrepareDelegate };
    }
}

/// <summary>Fluent composer for a migration's steps.</summary>
public sealed class KVMigrationBuilder
{
    internal List<IKVMigrationStep> Steps { get; } = new();

    internal Func<IReadOnlyList<KVMigrationSubject>, CancellationToken, Task<object?>>? PrepareDelegate { get; private set; }

    /// <summary>
    /// Declares the async batch phase for this migration: fetch external data for the whole subset in one go
    /// (avoiding N+1), returning a lookup that backfill factories read via <c>view.Context</c>. At most one
    /// prepare per migration.
    /// </summary>
    public KVMigrationBuilder Prepare(Func<IReadOnlyList<KVMigrationSubject>, CancellationToken, Task<object?>> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        if (PrepareDelegate is not null)
            throw new InvalidOperationException("A migration can declare at most one Prepare phase.");
        PrepareDelegate = prepare;
        return this;
    }

    /// <summary>Fills a field where unset (or, with <paramref name="overwrite"/>, everywhere it matches).</summary>
    public KVMigrationBuilder Backfill(KVTarget target, Func<KVMigrationFieldView, object?> value, bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);
        Steps.Add(new BackfillStep(target, value, overwrite));
        return this;
    }

    /// <summary>Removes a field / group / collection / nested node (tombstones the subtree).</summary>
    public KVMigrationBuilder Remove(KVTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Steps.Add(new RemoveStep(target));
        return this;
    }

    /// <summary>Renames a leaf field to a new segment under the same parent (honours the target's $type filter).</summary>
    public KVMigrationBuilder RenameField(KVTarget target, string toSegment)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSegment);
        Steps.Add(new RenameFieldStep(target, toSegment));
        return this;
    }

    /// <summary>
    /// Rewrites a whole subtree from one literal path to another — a root-level group / collection key rename,
    /// or a cross-parent field move (<c>Section1/Field1</c> → <c>Section2/Field1</c>).
    /// </summary>
    public KVMigrationBuilder RenameSegment(string fromPath, string toPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(toPath);
        Steps.Add(new RenameSegmentStep(fromPath, toPath));
        return this;
    }

    /// <summary>
    /// Renames the last segment of every subtree the target resolves to — wildcard-aware, so it renames a
    /// nested node / sub-collection living under collection-item ids that aren't known statically (e.g.
    /// rename <c>Kennels/*/Occupant</c> to <c>Kennels/*/Pet</c>). Carries all descendants.
    /// </summary>
    public KVMigrationBuilder RenameNode(KVTarget target, string toSegment)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSegment);
        Steps.Add(new RenameSubtreeStep(target, toSegment));
        return this;
    }

    /// <summary>
    /// Replaces the type of a nested node (e.g. CAT → DOG): swaps the <c>$type</c> discriminator and reshapes
    /// its fields — drop the old type's unique fields, add the new type's, leave common fields untouched. Only
    /// instances matching the target (typically filtered with <c>OfType(oldToken)</c>) are converted.
    /// </summary>
    public KVMigrationBuilder ReplaceNestedType(KVTarget target, string toType, Action<KVNodeReshapeBuilder>? reshape = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(toType);
        var reshapeBuilder = new KVNodeReshapeBuilder();
        reshape?.Invoke(reshapeBuilder);
        Steps.Add(new ReplaceNestedTypeStep(target, toType, reshapeBuilder.Ops));
        return this;
    }
}
