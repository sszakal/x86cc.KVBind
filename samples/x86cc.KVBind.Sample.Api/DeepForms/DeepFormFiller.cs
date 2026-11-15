using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api.DeepForms;

/// <summary>
/// Builds the patches that grow a component tree of a given <see cref="DeepFormShape"/> and fill every field
/// with valid values — what a client building the tree would send. Item ids follow the position, e.g.
/// <c>c2-1-2</c> is the second component of the first component of the second top-level one. Used by the
/// integration tests, the benchmarks and the load run.
/// </summary>
public static class DeepFormFiller
{
    private static readonly string[] Categories = ["mechanical", "electrical", "software", "service"];
    private static readonly string[] Tags = ["critical", "spare", "custom", "imported"];
    private static readonly string[] Units = ["mm", "kg", "V", "A"];

    /// <summary>Every component of the shape, parents before their children.</summary>
    public static IEnumerable<Slot> Slots(DeepFormShape shape)
    {
        IEnumerable<Slot> Level(string collectionPath, string prefix, int level, int count)
        {
            for (var i = 1; i <= count; i++)
            {
                var id = $"{prefix}{i}";
                var slot = new Slot(collectionPath, id, level, IsLeaf: level == shape.Depth);
                yield return slot;
                if (!slot.IsLeaf)
                {
                    foreach (var child in Level($"{slot.Path}/Detail/Components", $"{id}-", level + 1, shape.Branching))
                    {
                        yield return child;
                    }
                }
            }
        }

        return Level("/Components", "c", 1, shape.TopLevel);
    }

    /// <summary>The whole tree: every component added, its detail initialized, every field set.</summary>
    public static IEnumerable<KVPatchOperation> Build(DeepFormShape shape, int variant = 0)
    {
        foreach (var slot in Slots(shape))
        {
            // The required Name right after the ADD, so a commit between any two operations is valid.
            var values = Values(shape, slot, variant).ToList();
            yield return KVPatchOperation.Add(slot.CollectionPath, new KVAddPatchPayload(slot.Id));
            yield return values[0];
            for (var m = 1; m <= shape.Measurements; m++)
            {
                yield return KVPatchOperation.Add($"{slot.Path}/Measurements", new KVAddPatchPayload($"m{m}"));
            }

            yield return KVPatchOperation.Init($"{slot.Path}/Detail", slot.IsLeaf ? "PURCHASED" : "ASSEMBLY");
            foreach (var operation in values.Skip(1))
            {
                yield return operation;
            }
        }
    }

    /// <summary>A SET for every editable field of an already built tree (no structural changes).</summary>
    public static IEnumerable<KVPatchOperation> SetAll(DeepFormShape shape, int variant) =>
        Slots(shape).SelectMany(slot => Values(shape, slot, variant));

    /// <summary>A SET of the unit cost of every leaf: the change that ripples up every assembly.</summary>
    public static IEnumerable<KVPatchOperation> SetLeafCosts(DeepFormShape shape, decimal unitCost) =>
        Slots(shape).Where(slot => slot.IsLeaf).Select(slot => KVPatchOperation.Set($"{slot.Path}/UnitCost", unitCost));

    /// <summary>The deepest component's path (the last leaf).</summary>
    public static Slot DeepestLeaf(DeepFormShape shape) => Slots(shape).Last(slot => slot.IsLeaf);

    /// <summary>
    /// The total the root should show for a tree built with <paramref name="variant"/>: leaves cost
    /// quantity × unit cost + price, assemblies quantity × unit cost + what their components cost.
    /// </summary>
    public static decimal ExpectedTotal(DeepFormShape shape, int variant = 0)
    {
        var quantity = 1 + (variant % 3);
        var unitCost = 2m + variant;
        var price = 10m + variant;

        decimal Cost(int level) => level == shape.Depth
            ? (quantity * unitCost) + price
            : (quantity * unitCost) + (shape.Branching * Cost(level + 1));

        return shape.TopLevel * Cost(1);
    }

    // Name first: Build relies on it.
    private static IEnumerable<KVPatchOperation> Values(DeepFormShape shape, Slot slot, int variant)
    {
        var path = slot.Path;
        var n = variant + slot.Level;
        yield return KVPatchOperation.Set($"{path}/Name", $"Component {slot.Id} v{variant}");
        yield return KVPatchOperation.Set($"{path}/Quantity", 1 + (variant % 3));
        yield return KVPatchOperation.Set($"{path}/UnitCost", 2m + variant);
        yield return KVPatchOperation.Set($"{path}/Active", variant % 2 == 0);
        yield return KVPatchOperation.Set($"{path}/DueDate", new DateOnly(2027, 1, 1).AddDays(n));
        yield return KVPatchOperation.Set($"{path}/Category", Categories[n % Categories.Length]);
        yield return KVPatchOperation.Set($"{path}/Tags", new[] { Tags[n % Tags.Length] });
        yield return KVPatchOperation.Set($"{path}/Code", "ABC-123");
        yield return KVPatchOperation.Set($"{path}/Margin", 10m + variant);
        yield return KVPatchOperation.Set($"{path}/Notes", $"Notes for {slot.Id}, variant {variant}");

        for (var m = 1; m <= shape.Measurements; m++)
        {
            var measurement = $"{path}/Measurements/m{m}";
            yield return KVPatchOperation.Set($"{measurement}/Label", $"Measure {m}");
            yield return KVPatchOperation.Set($"{measurement}/Value", m + variant + 0.5m);
            yield return KVPatchOperation.Set($"{measurement}/Unit", Units[(m + variant) % Units.Length]);
        }

        var detail = $"{path}/Detail";
        if (slot.IsLeaf)
        {
            yield return KVPatchOperation.Set($"{detail}/Supplier", $"Supplier {variant}");
            yield return KVPatchOperation.Set($"{detail}/PartNumber", "AB123456");
            yield return KVPatchOperation.Set($"{detail}/LeadTimeDays", 14 + variant);
            yield return KVPatchOperation.Set($"{detail}/Price", 10m + variant);
        }
        else
        {
            yield return KVPatchOperation.Set($"{detail}/AssemblyCode", "ASM-0001");
            yield return KVPatchOperation.Set($"{detail}/Revision", $"r{variant}");
            yield return KVPatchOperation.Set($"{detail}/Weight", 5m + variant);
        }
    }

    /// <summary>One component of the tree: where its collection is, its id and level (1 = top).</summary>
    public sealed record Slot(string CollectionPath, string Id, int Level, bool IsLeaf)
    {
        public string Path => $"{this.CollectionPath}/{this.Id}";
    }
}
