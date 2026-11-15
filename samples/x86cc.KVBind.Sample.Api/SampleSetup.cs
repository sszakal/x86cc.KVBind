using Marten;
using Weasel.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.Claims;
using x86cc.KVBind.Sample.Api.DeepForms;
using x86cc.KVBind.Sample.Api.LargeForms;
using x86cc.KVBind.Sample.Api.Persistence;
using x86cc.KVBind.Sample.Api.Underwriting;

namespace x86cc.KVBind.Sample.Api;

/// <summary>
/// The sample's composition, shared by Program.cs and the integration tests so both store and bind data the
/// same way: the Marten document mapping, the definition registry and the model catalog.
/// </summary>
public static class SampleSetup
{
    public static void ConfigureMarten(StoreOptions options, string connectionString)
    {
        options.Connection(connectionString);

        // Pin the serializer explicitly (Marten 8+ already defaults to STJ) so a future default change can't
        // silently switch it, and to fix enum/casing conventions for the jsonb representation in one place.
        options.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase);

        // Optimistic concurrency: every Store of a loaded snapshot or draft checks that nobody saved it in the
        // meantime. Without it, two concurrent commits both pass the stale-draft check and the later one
        // silently overwrites the earlier (forking the commit chain), and concurrent patches lose updates.
        options.Schema.For<AggregateSnapshotDocument>().Identity(x => x.Id).UseOptimisticConcurrency(true).Index(x => x.Model);
        options.Schema.For<AggregateDraftDocument>().Identity(x => x.Id).UseOptimisticConcurrency(true);
        options.Schema.For<AggregateChangeSetDocument>().Identity(x => x.Id).Index(x => x.AggregateId);
    }

    /// <summary>Every model definition the sample serves; add new models here.</summary>
    public static IKVModelDefinitionBuilder[] DefinitionBuilders() =>
    [
        new InsuranceClaimDefinitionBuilder(),
        new UnderwritingDefinitionBuilder(),
        .. LargeFormModels.DefinitionBuilders(),
        .. DeepFormModels.DefinitionBuilders(),
    ];

    public static IEnumerable<IAggregateModel> Models(IKVDefinitionRegistry registry) =>
    [
        ClaimModel.Create(registry),
        UnderwritingModel.Create(registry),
        .. LargeFormModels.Create(registry),
        .. DeepFormModels.Create(registry),
    ];

    public static AggregateCatalog CreateCatalog(out IKVDefinitionRegistry registry)
    {
        registry = new KVDefinitionRegistry(DefinitionBuilders());
        return new AggregateCatalog(Models(registry));
    }
}
