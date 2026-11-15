using System.Collections.Concurrent;
using x86cc.KVBind.Core;
using x86cc.KVBind.Schema;

namespace x86cc.KVBind.Sample.Api.Aggregates;

/// <summary>
/// A model's schema, generated once from its real definition (x86cc.KVBind.Schema), plus the lookups the
/// change history needs: a data path's labels (to show "Policy/Coverage Type") and the labels of its allowed
/// values (to show "Collision" instead of the stored "collision"). Lookups walk the schema along the path,
/// following the references a recursive definition leaves (<see cref="KVSchemaReferences"/>), so they work
/// at any depth. Schema paths carry no item ids, e.g. <c>DamagedItems/Category</c>.
/// </summary>
public sealed class ModelSchema
{
    private static readonly HashSet<string> ReservedSegments = new(StringComparer.Ordinal) { "$type", "$items" };

    private readonly IReadOnlyDictionary<string, KVItemTypeSchema> types;
    private readonly ConcurrentDictionary<KVFieldSchema, IReadOnlyDictionary<string, string>> valueLabels = new(); // by reference

    public ModelSchema(KVNodeDefinition definition, string modelName)
    {
        this.Document = KVSchemaMapper.Map(definition, modelName);
        this.types = KVSchemaReferences.Index(this.Document);
    }

    public KVSchemaDocument Document { get; }

    /// <summary>
    /// Drops item ids and reserved segments from a data path by walking the schema, e.g.
    /// <c>DamagedItems/3f2c…/Category</c> → <c>DamagedItems/Category</c>. Item ids can be any valid string
    /// (seeded items use readable ids), so they are recognized by position, not by shape.
    /// </summary>
    public string ToSchemaPath(string path) => this.Walk(path).SchemaPath;

    /// <summary>True when the data path addresses a field (a leaf value), not a node or collection.</summary>
    public bool IsField(string path) => this.Walk(path).Field is not null;

    /// <summary>The data path with each segment replaced by its label where the schema declares one.</summary>
    public string ToDisplayPath(string path) => this.Walk(path).DisplayPath;

    /// <summary>Maps a stored allowed-value token (or array of tokens) to its label; other values pass through.</summary>
    public object? LabelValue(string path, object? value)
    {
        if (value is null || this.Walk(path).Field is not { AllowedValues: not null } field)
        {
            return value;
        }

        var labels = this.valueLabels.GetOrAdd(field, static field => field.AllowedValues!.Options
            .GroupBy(option => option.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal));

        if (value is string token)
        {
            return labels.TryGetValue(token, out var label) ? label : value;
        }

        if (value is System.Collections.IEnumerable sequence)
        {
            return sequence.Cast<object?>()
                .Select(element => element?.ToString() ?? string.Empty)
                .Select(element => labels.TryGetValue(element, out var label) ? label : element)
                .ToArray();
        }

        return value;
    }

    private static string? NonEmpty(string? label) => string.IsNullOrWhiteSpace(label) ? null : label;

    // Walks the schema along a data path. The candidate nodes are a list because a collection or nested
    // slot can hold several types; a segment matches a member in any of them.
    private Walked Walk(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var schemaPath = new List<string>(segments.Length);
        var display = new List<string>(segments.Length);
        IReadOnlyList<KVNodeSchema> nodes = [this.Document.Root];
        KVFieldSchema? field = null;
        var nextIsItemId = false;

        foreach (var segment in segments)
        {
            if (ReservedSegments.Contains(segment))
            {
                continue;
            }

            if (nextIsItemId)
            {
                nextIsItemId = false;
                display.Add(segment); // item ids stay as they are; the UI shows them as "Item #N"
                continue;
            }

            schemaPath.Add(segment);
            var step = this.Descend(nodes, segment);
            display.Add(step.Label ?? segment);
            nodes = step.Nodes;
            nextIsItemId = step.IsCollection;
            field = step.Field;
        }

        return new Walked(string.Join('/', schemaPath), string.Join('/', display), field);
    }

    private Step Descend(IReadOnlyList<KVNodeSchema> nodes, string segment)
    {
        var next = new List<KVNodeSchema>();
        string? label = null;
        var isCollection = false;
        KVFieldSchema? field = null;

        foreach (var node in nodes)
        {
            if (node.Fields.Find(candidate => candidate.Key == segment) is { } found)
            {
                field ??= found;
                label ??= NonEmpty(found.Label);
            }

            if (node.Groups.Find(group => group.Key == segment) is { } group)
            {
                label ??= NonEmpty(group.Label);
                next.Add(group);
            }

            if (node.Collections.Find(collection => collection.Key == segment) is { } collection)
            {
                label ??= NonEmpty(collection.Label);
                isCollection = true;
                next.AddRange(collection.ItemTypes.Select(type => KVSchemaReferences.NodeOf(type, this.types)));
            }

            if (node.NestedNodes.Find(nested => nested.Key == segment) is { } nestedNode)
            {
                label ??= NonEmpty(nestedNode.Label);
                next.AddRange(nestedNode.Types.Select(type => KVSchemaReferences.NodeOf(type, this.types)));
            }
        }

        return new Step(next, label, isCollection, field);
    }

    private readonly record struct Walked(string SchemaPath, string DisplayPath, KVFieldSchema? Field);

    private readonly record struct Step(IReadOnlyList<KVNodeSchema> Nodes, string? Label, bool IsCollection, KVFieldSchema? Field);
}
