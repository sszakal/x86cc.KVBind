using Marten;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCors(options =>
{
#pragma warning disable S5122 // Cross-Origin Resource Sharing (CORS) policy should be restricted to trusted origins
    options.AddPolicy("AngularDev", policy => policy
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Server-Timing")); // the UI's performance panel reads the stage timings
#pragma warning restore S5122 // Cross-Origin Resource Sharing (CORS) policy should be restricted to trusted origins
});

var kvbindConnectionString = builder.Configuration.GetConnectionString("kvbind")
    ?? throw new InvalidOperationException("Connection string 'kvbind' is required. Run the API through the Aspire AppHost.");

builder.AddNpgsqlDataSource("kvbind");

builder.Services.AddMarten(options => SampleSetup.ConfigureMarten(options, kvbindConnectionString));

// The definition registry and the catalog of models served under /api/models/{name}. Each model's schema
// is generated once from its definition (x86cc.KVBind.Schema) and served as-is. Add models in SampleSetup.
builder.Services.AddSingleton<IKVDefinitionRegistry>(_ => new KVDefinitionRegistry(SampleSetup.DefinitionBuilders()));
builder.Services.AddSingleton(sp => new AggregateCatalog(SampleSetup.Models(sp.GetRequiredService<IKVDefinitionRegistry>())));
builder.Services.AddScoped<AggregateService>();

// Per-request stage timings: Server-Timing header + OpenTelemetry spans.
builder.Services.AddScoped<RequestTimings>();

// The schema model is serializer-neutral; this API sends it as JSON with enum kinds as names.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Invalid requests (unknown patch path, wrong value type, missing user, ...) surface from KVBind and the
// service as InvalidOperationException / ArgumentException; a concurrent save as a Marten
// ConcurrencyException. Map them to 400 / 409 instead of a 500 with a stack trace.
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

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

app.UseServerTiming();
app.UseExceptionHandler();

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.MapAggregateEndpoints();

await app.RunAsync();
