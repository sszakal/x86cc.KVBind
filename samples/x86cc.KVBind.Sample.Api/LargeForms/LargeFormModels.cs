using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Sample.Api.Aggregates;

namespace x86cc.KVBind.Sample.Api.LargeForms;

/// <summary>Validation profile of a submitted large form.</summary>
public sealed record LargeFormSubmitProfile : KVValidationProfile;

public sealed record LargeFormSubmitted(int LineCount, decimal LinesTotal);

/// <summary>
/// Registers the generated large forms (LargeForm{N}.Generated.cs, from samples/tools/GenerateLargeForms.cs)
/// as <c>/api/models/large-{N}</c>: the same model at 100, 500, 1000 and 2000 section fields, so
/// performance can be shown as a curve rather than a single number.
/// </summary>
public static class LargeFormModels
{
    public static readonly int[] Sizes = [100, 500, 1000, 2000];

    public static IKVModelDefinitionBuilder[] DefinitionBuilders() =>
    [
        new LargeForm100DefinitionBuilder(),
        new LargeForm500DefinitionBuilder(),
        new LargeForm1000DefinitionBuilder(),
        new LargeForm2000DefinitionBuilder(),
    ];

    public static IEnumerable<IAggregateModel> Create(IKVDefinitionRegistry registry) =>
    [
        Model<LargeForm100>(100, registry, root => root.IsSubmitted, root => (root.LinesTotal, root.LineCount)),
        Model<LargeForm500>(500, registry, root => root.IsSubmitted, root => (root.LinesTotal, root.LineCount)),
        Model<LargeForm1000>(1000, registry, root => root.IsSubmitted, root => (root.LinesTotal, root.LineCount)),
        Model<LargeForm2000>(2000, registry, root => root.IsSubmitted, root => (root.LinesTotal, root.LineCount)),
    ];

    public static string NameOf(int size) => $"large-{size}";

    private static AggregateModel<TRoot> Model<TRoot>(
        int size,
        IKVDefinitionRegistry registry,
        Func<TRoot, bool> isSubmitted,
        Func<TRoot, (decimal Total, int Count)> lines)
        where TRoot : KVRootNode, new() => new(
        NameOf(size),
        $"Large Form · {size} fields",
        registry,
        createFields: ["S001/F09Text"],
        summaryFields: ["S001/F09Text", "Status", "LineCount", "LinesTotal"],
        description: $"Generated: {size / 25} sections × 25 fields of ten kinds, plus up to 1,000 line items, a nested party, a reaction and an operation. For the performance demo.",
        profileName: root => isSubmitted(root) ? "submit" : "default",
        computed: root => new Dictionary<string, object?>(StringComparer.Ordinal) { ["Fields"] = size, ["Lines"] = lines(root).Count },
        derivedPaths: ["LinesTotal", "LineCount"]);
}
