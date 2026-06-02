var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("kvbind")))
{
    builder.AddNpgsqlDataSource("kvbind");
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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

app.Run();

public sealed record DemoStatusResponse(string Name, bool PostgresConfigured);
