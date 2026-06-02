using System;
using System.Collections.Generic;

namespace x86cc.KVBind.Core;

public sealed class KVPatchResult(IReadOnlyList<KVChangeDelta> changes, Func<KVValidationResult>? validate = null)
{
    public IReadOnlyList<KVChangeDelta> Changes { get; } = changes;

    public KVValidationResult Validate() => validate?.Invoke() ?? new KVValidationResult([], [], isFullEvaluation: false);
}
