using Marten;
using Testcontainers.PostgreSql;
using Weasel.Core;
using x86cc.KVBind.IntegrationTests.Persistence;

namespace x86cc.KVBind.IntegrationTests.Fixtures;

public abstract class PostgresMartenTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6")
        .WithDatabase("kvbind_integration")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected IDocumentStore Store { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Store = DocumentStore.For(options =>
        {
            options.Connection(_postgres.GetConnectionString());
            // Match the API: pin STJ and the enum/casing conventions so tests serialize as production does.
            options.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase);
            options.Schema.For<IntegrationSnapshotDocument>().Identity(x => x.Id);
            options.Schema.For<IntegrationOverlayDocument>().Identity(x => x.Id);
            options.Schema.For<IntegrationCommitDocument>().Identity(x => x.Id);
        });
    }

    public async Task DisposeAsync()
    {
        Store.Dispose();
        await _postgres.DisposeAsync();
    }
}
