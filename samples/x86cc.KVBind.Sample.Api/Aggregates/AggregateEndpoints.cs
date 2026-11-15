namespace x86cc.KVBind.Sample.Api.Aggregates;

public static class AggregateEndpoints
{
    /// <summary>
    /// Maps <c>/api/models</c> (the catalog) and <c>/api/models/{model}/...</c> for every model, plus
    /// <c>/api/claims/...</c> as an alias of <c>/api/models/claims/...</c> for existing clients.
    /// </summary>
    public static IEndpointRouteBuilder MapAggregateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/models", (AggregateCatalog catalog) =>
                Results.Ok(catalog.All.Select(ToInfo).ToArray()))
            .WithTags("Models")
            .WithName("ListModelCatalog")
            .WithSummary("Lists the models this API serves.");

        var models = endpoints.MapGroup("/api/models/{model}").WithTags("Models");
        MapModelRoutes(models, "Model", context => context.Request.RouteValues["model"] as string);

        var claims = endpoints.MapGroup("/api/claims").WithTags("Insurance Claims");
        MapModelRoutes(claims, "Claim", _ => "claims");

        return endpoints;
    }

    private static ModelInfoResponse ToInfo(IAggregateModel model) => new(
        model.Name,
        model.Title,
        model.Description,
        model.Definition.CurrentSchemaVersion,
        model.CreateFields,
        model.SummaryFields,
        model.SupportsChildren,
        model.LegacySnapshot is not null);

    // Resolves the model of a request, or answers 404 for an unknown one.
    private static IResult WithModel(HttpContext context, Func<HttpContext, string?> modelName, Func<IAggregateModel, IResult> handle)
    {
        var catalog = context.RequestServices.GetRequiredService<AggregateCatalog>();
        var model = modelName(context) is { } name ? catalog.Find(name) : null;
        return model is null ? Results.NotFound(new { error = $"Unknown model '{modelName(context)}'." }) : handle(model);
    }

    private static async Task<IResult> WithModelAsync(HttpContext context, Func<HttpContext, string?> modelName, Func<IAggregateModel, Task<IResult>> handle)
    {
        var catalog = context.RequestServices.GetRequiredService<AggregateCatalog>();
        var model = modelName(context) is { } name ? catalog.Find(name) : null;
        return model is null ? Results.NotFound(new { error = $"Unknown model '{modelName(context)}'." }) : await handle(model);
    }

    private static IResult OkOrNotFound(object? response) => response is null ? Results.NotFound() : Results.Ok(response);

    private static void MapModelRoutes(RouteGroupBuilder group, string namePrefix, Func<HttpContext, string?> modelName)
    {
        group.MapGet("/info", (HttpContext context) =>
                WithModel(context, modelName, model => Results.Ok(ToInfo(model))))
            .WithName($"Get{namePrefix}Info")
            .WithSummary("Describes the model: title, create and summary fields, schema version.");

        group.MapGet("/definition", (HttpContext context) =>
                WithModel(context, modelName, model => Results.Ok(model.Schema.Document)))
            .WithName($"Get{namePrefix}Definition")
            .WithSummary("Returns the model's schema, generated from its KVBind definition (x86cc.KVBind.Schema).");

        group.MapPost("/", (HttpContext context, CreateAggregateRequest request, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model =>
                {
                    var response = await service.CreateAsync(model, request, cancellationToken);
                    return Results.Created($"{context.Request.Path.Value?.TrimEnd('/')}/{response.Id}/snapshot", response);
                }))
            .WithName($"Create{namePrefix}")
            .WithSummary("Creates an aggregate (optionally as a child of another) with its first commit.");

        group.MapGet("/", (HttpContext context, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => Results.Ok(await service.ListAsync(model, cancellationToken))))
            .WithName($"List{namePrefix}s")
            .WithSummary("Lists the stored aggregates with their summary fields.");

        group.MapPost("/legacy", (HttpContext context, OpenDraftRequest request, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => Results.Ok(await service.CreateLegacyAsync(model, request.User, cancellationToken))))
            .WithName($"Create{namePrefix}Legacy")
            .WithSummary("Stores an aggregate in the model's old layout (an older schema version), to demonstrate migrations.");

        group.MapPost("/migrate", (HttpContext context, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => Results.Ok(await service.MigrateAllAsync(model, cancellationToken))))
            .WithName($"Migrate{namePrefix}s")
            .WithSummary("Migrates every stored aggregate to the current schema version in one batch.");

        group.MapGet("/{id:guid}/snapshot", (HttpContext context, Guid id, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.GetSnapshotAsync(model, id, cancellationToken))))
            .WithName($"Get{namePrefix}Snapshot")
            .WithSummary("Gets the committed snapshot (migrating it first when it is behind).");

        group.MapGet("/{id:guid}/changesets", (HttpContext context, Guid id, int? offset, int? limit, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => Results.Ok(await service.ListChangeSetsAsync(model, id, offset ?? 0, limit ?? 50, cancellationToken))))
            .WithName($"List{namePrefix}ChangeSets")
            .WithSummary("Lists a page of the commits of an aggregate, newest first, with their changes and domain events (?offset=0&limit=50, at most 500).");

        group.MapPost("/{id:guid}/drafts", (HttpContext context, Guid id, OpenDraftRequest request, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model =>
                {
                    var response = await service.OpenDraftAsync(model, id, request, cancellationToken);
                    return response is null
                        ? Results.NotFound()
                        : Results.Created($"{context.Request.Path.Value?.TrimEnd('/')}/{response.DraftId}", response);
                }))
            .WithName($"Open{namePrefix}Draft")
            .WithSummary("Opens (or resumes) the user's draft of an aggregate.");

        group.MapGet("/{id:guid}/drafts/{draftId:guid}", (HttpContext context, Guid id, Guid draftId, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.GetDraftAsync(model, id, draftId, cancellationToken))))
            .WithName($"Get{namePrefix}Draft")
            .WithSummary("Gets a draft with its data, changes and rebase state.");

        group.MapPost("/{id:guid}/drafts/{draftId:guid}/patch", (HttpContext context, Guid id, Guid draftId, IReadOnlyList<PatchOperationRequest> request, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.PatchDraftAsync(model, id, draftId, request, cancellationToken))))
            .WithName($"Patch{namePrefix}Draft")
            .WithSummary("Applies KVBind patch operations (built-in or custom) to a draft.");

        group.MapPost("/{id:guid}/drafts/{draftId:guid}/validate", (HttpContext context, Guid id, Guid draftId, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.ValidateDraftAsync(model, id, draftId, cancellationToken))))
            .WithName($"Validate{namePrefix}Draft")
            .WithSummary("Validates the draft with the profile its current state selects.");

        group.MapPost("/{id:guid}/drafts/{draftId:guid}/reset", (HttpContext context, Guid id, Guid draftId, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.ResetDraftAsync(model, id, draftId, cancellationToken))))
            .WithName($"Reset{namePrefix}Draft")
            .WithSummary("Drops all draft changes and resyncs the draft onto the latest commit.");

        group.MapPost("/{id:guid}/drafts/{draftId:guid}/commit", (HttpContext context, Guid id, Guid draftId, CommitDraftRequest request, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model =>
                {
                    var result = await service.CommitDraftAsync(model, id, draftId, request, cancellationToken);
                    return result switch
                    {
                        null => Results.NotFound(),
                        { StaleDraft: { } stale } => Results.Conflict(stale),
                        { Invalid: { } invalid } => Results.UnprocessableEntity(invalid),
                        _ => Results.Ok(result.Commit)
                    };
                }))
            .WithName($"Commit{namePrefix}Draft")
            .WithSummary("Commits the draft: 409 when it is stale (rebase first), 422 with the errors when it is invalid.");

        group.MapPost("/{id:guid}/drafts/{draftId:guid}/rebase", (HttpContext context, Guid id, Guid draftId, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.BeginRebaseAsync(model, id, draftId, cancellationToken))))
            .WithName($"Begin{namePrefix}DraftRebase")
            .WithSummary("Starts a rebase onto the latest commit; returns incoming changes and conflicts.");

        group.MapPost("/{id:guid}/drafts/{draftId:guid}/rebase/resolve", (HttpContext context, Guid id, Guid draftId, ResolveRebaseConflictRequest request, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.ResolveRebaseConflictAsync(model, id, draftId, request, cancellationToken))))
            .WithName($"Resolve{namePrefix}DraftRebaseConflict")
            .WithSummary("Resolves one rebase conflict (Ours, Theirs or Custom).");

        group.MapPost("/{id:guid}/drafts/{draftId:guid}/rebase/finish", (HttpContext context, Guid id, Guid draftId, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.FinishRebaseAsync(model, id, draftId, cancellationToken))))
            .WithName($"Finish{namePrefix}DraftRebase")
            .WithSummary("Finishes the rebase once every conflict is resolved.");

        group.MapDelete("/{id:guid}/drafts/{draftId:guid}/rebase", (HttpContext context, Guid id, Guid draftId, AggregateService service, CancellationToken cancellationToken) =>
                WithModelAsync(context, modelName, async model => OkOrNotFound(await service.CancelRebaseAsync(model, id, draftId, cancellationToken))))
            .WithName($"Cancel{namePrefix}DraftRebase")
            .WithSummary("Aborts the rebase, keeping the draft changes on their original base.");
    }
}
