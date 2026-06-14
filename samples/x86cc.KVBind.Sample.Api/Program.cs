using Marten;
using Weasel.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Sample.Api.Claims;
using x86cc.KVBind.Sample.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularDev", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var kvbindConnectionString = builder.Configuration.GetConnectionString("kvbind")
    ?? throw new InvalidOperationException("Connection string 'kvbind' is required. Run the API through the Aspire AppHost.");

builder.AddNpgsqlDataSource("kvbind");

builder.Services.AddMarten(options =>
{
    options.Connection(kvbindConnectionString);
    // Pin the serializer explicitly (Marten 8+ already defaults to STJ) so a future default change can't
    // silently switch it, and to fix enum/casing conventions for the jsonb representation in one place.
    options.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase);
    options.Schema.For<ClaimSnapshotDocument>().Identity(x => x.Id);
    options.Schema.For<ClaimOverlayDocument>().Identity(x => x.Id);
    options.Schema.For<ClaimChangeSetDocument>().Identity(x => x.Id);
});

// Register each model's definition builder — add more here as new models are introduced.
builder.Services.AddSingleton<IKVModelDefinitionBuilder, InsuranceClaimDefinitionBuilder>();

// The registry collects all registered builders and assembles itself.
builder.Services.AddSingleton<IKVDefinitionRegistry>(sp =>
    new KVDefinitionRegistry(sp.GetServices<IKVModelDefinitionBuilder>().ToArray()));

builder.Services.AddScoped<InsuranceClaimAggregateService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDev");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.MapGet("/api/demo/status", (IConfiguration configuration) =>
{
    return Results.Ok(new DemoStatusResponse(
        "KVBind sample API",
        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("kvbind"))));
})
.WithName("GetDemoStatus")
.WithSummary("Reports whether the sample API is configured with the Aspire PostgreSQL database.");

app.MapClaimEndpoints();

app.Run();

public sealed record DemoStatusResponse(string Name, bool PostgresConfigured);
