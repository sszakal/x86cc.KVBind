using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Sample.Api.Aggregates;

/// <summary>
/// One model the generic API serves under <c>/api/models/{name}</c>. Everything the service needs that the
/// KVBind definition does not already say: how to bind the typed root, which fields identify a new
/// aggregate and summarize it in a list, which validation profile is active, and any derived values.
/// </summary>
public interface IAggregateModel
{
    /// <summary>The route segment, e.g. <c>claims</c>.</summary>
    string Name { get; }

    string Title { get; }

    string? Description { get; }

    KVNodeDefinition Definition { get; }

    ModelSchema Schema { get; }

    /// <summary>Paths set when an aggregate is created (the create form).</summary>
    IReadOnlyList<string> CreateFields { get; }

    /// <summary>Paths shown in the aggregate list.</summary>
    IReadOnlyList<string> SummaryFields { get; }

    /// <summary>True when an aggregate may be created as a child of another aggregate of the same model.</summary>
    bool SupportsChildren { get; }

    /// <summary>
    /// Builds a snapshot in an old stored layout (an older schema version), so the sample can show its
    /// migrations running; null when the model has no migrations to demonstrate.
    /// </summary>
    Func<string, KVSnapshot>? LegacySnapshot { get; }

    /// <summary>
    /// Stored fields that change reactions derive from other data (e.g. a total). Two drafts that both
    /// changed the inputs both wrote them, so a rebase would report conflicts on them; the service resolves
    /// those as Theirs, and finishing the rebase recomputes them (NotifyReplaced). Entries are schema paths
    /// (no item ids, e.g. <c>Lines/Amount</c>), or <c>**/Key</c> for that field at any depth.
    /// </summary>
    IReadOnlyList<string> DerivedPaths { get; }

    KVRootNode Bind(KVOverlay overlay, KVSnapshot? parentSnapshot);

    /// <summary>Binds a brand-new aggregate, applying its declared defaults.</summary>
    KVRootNode BindNew(KVOverlay overlay);

    /// <summary>Creates a new child of <paramref name="parent"/> that inherits its inherited members.</summary>
    KVRootNode CreateChild(KVRootNode parent);

    /// <summary>The name of the validation profile <see cref="KVRootNode.Validate"/> uses for this state.</summary>
    string ProfileName(KVRootNode root);

    /// <summary>Values derived in code (not stored), shown next to the data, e.g. a computed total.</summary>
    IReadOnlyDictionary<string, object?> Computed(KVRootNode root);
}

public sealed class AggregateModel<TRoot> : IAggregateModel
    where TRoot : KVRootNode, new()
{
    private readonly Func<TRoot, string> profileName;
    private readonly Func<TRoot, IReadOnlyDictionary<string, object?>> computed;

    public AggregateModel(
        string name,
        string title,
        IKVDefinitionRegistry registry,
        IReadOnlyList<string> createFields,
        IReadOnlyList<string> summaryFields,
        string? description = null,
        Func<TRoot, string>? profileName = null,
        Func<TRoot, IReadOnlyDictionary<string, object?>>? computed = null,
        bool supportsChildren = false,
        Func<string, KVSnapshot>? legacySnapshot = null,
        IReadOnlyList<string>? derivedPaths = null)
    {
        this.Name = name;
        this.Title = title;
        this.Description = description;
        this.Definition = registry.Get<TRoot>();
        this.Schema = new ModelSchema(this.Definition, typeof(TRoot).Name);
        this.CreateFields = createFields;
        this.SummaryFields = summaryFields;
        this.SupportsChildren = supportsChildren;
        this.LegacySnapshot = legacySnapshot;
        this.DerivedPaths = derivedPaths ?? [];
        this.profileName = profileName ?? (_ => "default");
        this.computed = computed ?? (_ => new Dictionary<string, object?>(StringComparer.Ordinal));
    }

    public string Name { get; }

    public string Title { get; }

    public string? Description { get; }

    public KVNodeDefinition Definition { get; }

    public ModelSchema Schema { get; }

    public IReadOnlyList<string> CreateFields { get; }

    public IReadOnlyList<string> SummaryFields { get; }

    public bool SupportsChildren { get; }

    public Func<string, KVSnapshot>? LegacySnapshot { get; }

    public IReadOnlyList<string> DerivedPaths { get; }

    public KVRootNode Bind(KVOverlay overlay, KVSnapshot? parentSnapshot) =>
        parentSnapshot is null && !overlay.IsMarkedChild
            ? KVRootNode.Create<TRoot>(overlay, this.Definition)
            : KVRootNode.Create<TRoot>(overlay, this.Definition, parentSnapshot);

    public KVRootNode BindNew(KVOverlay overlay) => KVRootNode.CreateNew<TRoot>(overlay, this.Definition);

    public KVRootNode CreateChild(KVRootNode parent) => parent.CreateChild<TRoot>();

    public string ProfileName(KVRootNode root) => this.profileName((TRoot)root);

    public IReadOnlyDictionary<string, object?> Computed(KVRootNode root) => this.computed((TRoot)root);
}

/// <summary>The models the API serves, by route name.</summary>
public sealed class AggregateCatalog(IEnumerable<IAggregateModel> models)
{
    private readonly Dictionary<string, IAggregateModel> byName =
        models.ToDictionary(model => model.Name, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<IAggregateModel> All => this.byName.Values;

    public IAggregateModel? Find(string name) => this.byName.GetValueOrDefault(name);
}
