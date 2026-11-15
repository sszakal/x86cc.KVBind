using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

/// <summary>
/// The outcome of <see cref="KVRootNode.Patch(KVPatchOperation[])"/>: the changes that patch made, and the
/// draft's validation. <see cref="Changes"/> is fixed when the patch returns; for everything the draft holds
/// (earlier patches and sessions included) use <see cref="KVRootNode.GetAllChanges"/>.
/// </summary>
public sealed class KVPatchResult
{
    private readonly IReadOnlyList<KVChangeDelta> changes;
    private readonly Func<KVValidationResult>? validate;

    public KVPatchResult(IReadOnlyList<KVChangeDelta> changes, Func<KVValidationResult>? validate = null)
    {
        ArgumentNullException.ThrowIfNull(changes);
        this.changes = changes;
        this.validate = validate;
    }

    /// <summary>
    /// The changes this patch made, in the order they happened: each field it set or cleared (with its old and
    /// new value), each item or nested node it added, removed, moved or reset, and the writes of the change
    /// reactions it triggered. A path the patch added or removed stands for everything beneath it; a field
    /// written back to its old value is no change.
    /// </summary>
    public IReadOnlyList<KVChangeDelta> Changes => this.changes;

    public KVValidationResult Validate() => this.validate?.Invoke() ?? new KVValidationResult([], [], isFullEvaluation: false);
}
