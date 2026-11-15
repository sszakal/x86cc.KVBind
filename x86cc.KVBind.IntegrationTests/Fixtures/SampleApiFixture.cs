using Marten;
using Testcontainers.PostgreSql;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.Diagnostics;

namespace x86cc.KVBind.IntegrationTests.Fixtures;

/// <summary>
/// One Postgres container and Marten store, configured exactly like the sample API (SampleSetup), shared
/// by the API-level scenario tests. Each test drives <see cref="AggregateService"/> as the endpoints do,
/// with a fresh session per "request".
/// </summary>
public sealed class SampleApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17.6")
        .WithDatabase("kvbind_sample")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public IDocumentStore Store { get; private set; } = null!;

    public AggregateCatalog Catalog { get; private set; } = null!;

    public IKVDefinitionRegistry Registry { get; private set; } = null!;

    public IAggregateModel Model(string name) => this.Catalog.Find(name) ?? throw new InvalidOperationException(name);

    /// <summary>A service over a new session — one per simulated request, like the scoped API service.</summary>
    public AggregateService Service() => new(this.Store.LightweightSession(), new RequestTimings());

    public async Task InitializeAsync()
    {
        await this.postgres.StartAsync();
        this.Store = DocumentStore.For(options => SampleSetup.ConfigureMarten(options, this.postgres.GetConnectionString()));
        this.Catalog = SampleSetup.CreateCatalog(out var registry);
        this.Registry = registry;
    }

    public async Task DisposeAsync()
    {
        this.Store.Dispose();
        await this.postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SampleApiCollection : ICollectionFixture<SampleApiFixture>
{
    public const string Name = "Sample API (Postgres)";
}
