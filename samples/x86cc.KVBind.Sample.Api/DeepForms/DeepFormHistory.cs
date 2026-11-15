using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api.DeepForms;

/// <summary>
/// A long, realistic history for a component tree: the tree is built and filled a few edits per commit, then
/// every field keeps being re-edited (new values each pass), so any number of commits can be produced. Also
/// plans the drafts of other users who branched off earlier: each edits fields that later commits change too
/// (real conflicts) and adds a subtree of its own. Used by the history load run and the benchmarks.
/// </summary>
public static class DeepFormHistory
{
    // About a million edits on a 1,000-field tree: far more than any history the tools build.
    private const int MaxEditPasses = 1000;

    /// <summary>
    /// The commits, each a few operations. A component's ADD and its required Name stay in one commit, so
    /// every commit validates.
    /// </summary>
    public static IEnumerable<KVPatchOperation[]> Commits(DeepFormShape shape, int editsPerCommit, bool includeBuild = true)
    {
        var batch = new List<KVPatchOperation>();
        foreach (var operation in Stream(shape, includeBuild))
        {
            batch.Add(operation);
            if (batch.Count >= editsPerCommit && operation.OperationCode != KVPatchOperations.Add)
            {
                yield return [.. batch];
                batch.Clear();
            }
        }
    }

    /// <summary>
    /// The edits of a draft opened after <paramref name="history"/>[..<paramref name="openedAt"/>] and left
    /// behind by the next commits up to <paramref name="behindBy"/>: up to <paramref name="conflicts"/> fields
    /// those commits change too, set to a value of its own, plus a new top-level component with its own
    /// PURCHASED detail.
    /// </summary>
    public static KVPatchOperation[] StaleDraftEdits(
        IReadOnlyList<KVPatchOperation[]> history, int openedAt, int behindBy, string user, int conflicts = 20)
    {
        var existing = new HashSet<string>(
            history.Take(openedAt).SelectMany(commit => commit).Where(IsSet).Select(op => op.Path),
            StringComparer.Ordinal);
        var upcoming = history.Skip(openedAt).Take(behindBy).SelectMany(commit => commit)
            .Where(op => IsSet(op) && existing.Contains(op.Path))
            .DistinctBy(op => op.Path, StringComparer.Ordinal)
            .ToList();
        var step = Math.Max(1, upcoming.Count / Math.Max(1, conflicts));
        var edits = upcoming.Where((_, index) => index % step == 0).Take(conflicts)
            .Select(op => KVPatchOperation.Set(op.Path, Mutate(op.Path, op.Value)))
            .ToList();

        var id = $"{user}-part";
        edits.Add(KVPatchOperation.Add("/Components", new KVAddPatchPayload(id)));
        edits.Add(KVPatchOperation.Set($"/Components/{id}/Name", $"Added by {user}"));
        edits.Add(KVPatchOperation.Set($"/Components/{id}/UnitCost", 3m));
        edits.Add(KVPatchOperation.Init($"/Components/{id}/Detail", "PURCHASED"));
        edits.Add(KVPatchOperation.Set($"/Components/{id}/Detail/Price", 30m));
        return [.. edits];
    }

    private static IEnumerable<KVPatchOperation> Stream(DeepFormShape shape, bool includeBuild)
    {
        if (includeBuild)
        {
            foreach (var operation in DeepFormFiller.Build(shape))
            {
                yield return operation;
            }
        }

        // Each pass re-edits every field with new values; callers take as many commits as they need.
        for (var variant = 1; variant <= MaxEditPasses; variant++)
        {
            foreach (var operation in DeepFormFiller.SetAll(shape, variant))
            {
                yield return operation;
            }
        }
    }

    private static bool IsSet(KVPatchOperation operation) => operation.OperationCode == KVPatchOperations.Set;

    // A valid value different from the ones the history writes.
    private static object? Mutate(string path, object? value)
    {
        var key = path[(path.LastIndexOf('/') + 1)..];
        return (key, value) switch
        {
            ("Category", _) => "service",
            ("Unit", _) => "kg",
            ("Tags", _) => new[] { "critical", "imported" },
            ("Code", _) => "XYZ-999",
            ("PartNumber", _) => "ZZ999999",
            ("AssemblyCode", _) => "ASM-9999",
            ("Revision", _) => "draft",
            ("Quantity", int quantity) => quantity + 100,
            ("LeadTimeDays", int days) => 300 + (days % 50),
            (_, decimal number) => number + 0.25m,
            (_, int number) => number + 1,
            (_, bool flag) => !flag,
            (_, DateOnly date) => date.AddYears(1),
            (_, string text) => $"{text} (draft)",
            _ => value,
        };
    }
}
