using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Sample.Api.Aggregates;

namespace x86cc.KVBind.Sample.Api.DeepForms;

/// <summary>
/// The shape of a generated component tree: <see cref="TopLevel"/> components at the root, each assembly
/// holding <see cref="Branching"/> components, <see cref="Depth"/> levels in all. Leaves are PURCHASED parts,
/// everything above them an ASSEMBLY.
/// </summary>
public sealed record DeepFormShape(int TopLevel, int Branching, int Depth, int Measurements = 3)
{
    /// <summary>
    /// About 1,000 field values at each depth with the same 45 (42 at depth 3) components, so the shapes
    /// differ in depth only: 45 flat (1,084 values), 15 × 3 (1,069), 6 × 7 (994), 3 × 15 (1,063).
    /// </summary>
    public static readonly DeepFormShape[] ThousandFields =
    [
        new(45, 2, 1),
        new(15, 2, 2),
        new(6, 2, 3),
        new(3, 2, 4),
    ];

    public int Components => this.TopLevel * Enumerable.Range(0, this.Depth).Sum(level => (int)Math.Pow(this.Branching, level));

    public int Leaves => this.TopLevel * (int)Math.Pow(this.Branching, this.Depth - 1);

    /// <summary>
    /// Stored field values once filled: 10 per component, 3 per measurement, 4 per assembly (with its
    /// Subtotal) and 5 per purchased part, plus the root's 4.
    /// </summary>
    public int FieldValues =>
        4 + (this.Components * (10 + (3 * this.Measurements))) + ((this.Components - this.Leaves) * 4) + (this.Leaves * 5);

    public static DeepFormShape AtDepth(int depth) => ThousandFields.Single(shape => shape.Depth == depth);

    public override string ToString() => $"depth {this.Depth}: {this.TopLevel} × {this.Branching}^n";
}

/// <summary>Registers the recursive component tree as <c>/api/models/deep</c>.</summary>
public static class DeepFormModels
{
    public const string Name = "deep";

    public static IKVModelDefinitionBuilder[] DefinitionBuilders() => [new DeepFormDefinitionBuilder()];

    public static IEnumerable<IAggregateModel> Create(IKVDefinitionRegistry registry) =>
    [
        new AggregateModel<DeepForm>(
            Name,
            "Deep Tree · recursive components",
            registry,
            createFields: ["Title"],
            summaryFields: ["Title", "Status", "ComponentCount", "TotalCost"],
            description: "A bill of materials that can grow without limit: components (collection items) with their own measurements and an empty Detail slot; an ASSEMBLY in it holds components again. Costs roll up through every level.",
            computed: root =>
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Components"] = DeepForm.CountAll(root.Components),
                    ["Depth"] = DeepForm.DepthOf(root.Components),
                };
            },
            derivedPaths: ["TotalCost", "ComponentCount", "**/Subtotal"]),
    ];
}
