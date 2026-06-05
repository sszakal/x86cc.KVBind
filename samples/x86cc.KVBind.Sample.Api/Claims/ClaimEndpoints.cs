namespace x86cc.KVBind.Sample.Api.Claims;

public static class ClaimEndpoints
{
    public static IEndpointRouteBuilder MapClaimEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/claims")
            .WithTags("Insurance Claims");

        group.MapPost("/", async (
                CreateClaimRequest request,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CreateClaimAsync(request, cancellationToken);
                return Results.Created($"/api/claims/{response.ClaimId}/snapshot", response);
            })
            .WithName("CreateClaim")
            .WithSummary("Creates an insurance claim snapshot and initial changeset.");

        group.MapGet("/", async (InsuranceClaimAggregateService service, CancellationToken cancellationToken) =>
            {
                var response = await service.ListClaimsAsync(cancellationToken);
                return Results.Ok(response);
            })
            .WithName("ListClaims")
            .WithSummary("Lists persisted insurance claim snapshots.");

        group.MapGet("/{claimId:guid}/snapshot", async (
                Guid claimId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetSnapshotAsync(claimId, cancellationToken);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("GetClaimSnapshot")
            .WithSummary("Gets the committed snapshot for an insurance claim.");

        group.MapPost("/{claimId:guid}/drafts", async (
                Guid claimId,
                OpenClaimDraftRequest request,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.OpenDraftAsync(claimId, request, cancellationToken);
                return response is null ? Results.NotFound() : Results.Created($"/api/claims/{claimId}/drafts/{response.DraftId}", response);
            })
            .WithName("OpenClaimDraft")
            .WithSummary("Creates a persisted draft overlay for an insurance claim.");

        group.MapGet("/{claimId:guid}/drafts/{draftId:guid}", async (
                Guid claimId,
                Guid draftId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.GetDraftAsync(claimId, draftId, cancellationToken);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("GetClaimDraft")
            .WithSummary("Gets a persisted draft overlay for an insurance claim.");

        group.MapPost("/{claimId:guid}/drafts/{draftId:guid}/patch", async (
                Guid claimId,
                Guid draftId,
                IReadOnlyList<ClaimPatchOperationRequest> request,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.PatchDraftAsync(claimId, draftId, request, cancellationToken);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("PatchClaimDraft")
            .WithSummary("Applies canonical KVBind patch operations to a persisted draft overlay.");

        group.MapPost("/{claimId:guid}/drafts/{draftId:guid}/commit", async (
                Guid claimId,
                Guid draftId,
                CommitClaimDraftRequest request,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CommitDraftAsync(claimId, draftId, request, cancellationToken);
                if (response is null)
                {
                    return Results.NotFound();
                }

                return response.StaleDraft is not null
                    ? Results.Conflict(response.StaleDraft)
                    : Results.Ok(response.Commit);
            })
            .WithName("CommitClaimDraft")
            .WithSummary("Commits a persisted draft overlay into the claim snapshot and records a changeset.");

        group.MapPost("/{claimId:guid}/drafts/{draftId:guid}/rebase", async (
                Guid claimId,
                Guid draftId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.BeginRebaseAsync(claimId, draftId, cancellationToken);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("BeginClaimDraftRebase")
            .WithSummary("Starts a rebase of the draft onto the latest snapshot. Auto-merges when there are no conflicts; otherwise returns the conflict list.");

        group.MapPost("/{claimId:guid}/drafts/{draftId:guid}/rebase/resolve", async (
                Guid claimId,
                Guid draftId,
                ResolveRebaseConflictRequest request,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var response = await service.ResolveRebaseConflictAsync(claimId, draftId, request, cancellationToken);
                    return response is null ? Results.NotFound() : Results.Ok(response);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("ResolveClaimDraftRebaseConflict")
            .WithSummary("Resolves a single rebase conflict (Ours, Theirs or Custom).");

        group.MapPost("/{claimId:guid}/drafts/{draftId:guid}/rebase/finish", async (
                Guid claimId,
                Guid draftId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var response = await service.FinishRebaseAsync(claimId, draftId, cancellationToken);
                    return response is null ? Results.NotFound() : Results.Ok(response);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("FinishClaimDraftRebase")
            .WithSummary("Finishes the rebase — applies all resolutions and swaps the draft onto the target snapshot. Requires every conflict resolved.");

        group.MapDelete("/{claimId:guid}/drafts/{draftId:guid}/rebase", async (
                Guid claimId,
                Guid draftId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.CancelRebaseAsync(claimId, draftId, cancellationToken);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("CancelClaimDraftRebase")
            .WithSummary("Aborts the rebase, keeping the draft changes on the original (stale) base.");

        group.MapPost("/{claimId:guid}/drafts/{draftId:guid}/reset", async (
                Guid claimId,
                Guid draftId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.ResetDraftAsync(claimId, draftId, cancellationToken);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("ResetClaimDraft")
            .WithSummary("Drops all draft changes and resyncs the overlay onto the latest snapshot.");

        group.MapGet("/schema", () => Results.Ok(InsuranceClaimAggregateService.GetSchema()))
            .WithName("GetClaimSchema")
            .WithSummary("Returns allowed values for all constrained fields.");

        group.MapGet("/definition", (InsuranceClaimAggregateService service) => Results.Ok(service.GetDefinitionSchema()))
            .WithName("GetClaimDefinition")
            .WithSummary("Returns the full field definition — drives auto-generated form rendering. Labels come from the DSL DisplayName(...).");

        group.MapPost("/{claimId:guid}/drafts/{draftId:guid}/validate", async (
                Guid claimId,
                Guid draftId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.ValidateDraftAsync(claimId, draftId, cancellationToken);
                return response is null ? Results.NotFound() : Results.Ok(response);
            })
            .WithName("ValidateClaimDraft")
            .WithSummary("Validates the draft. Profile is selected automatically based on the claim's current status.");

        group.MapGet("/{claimId:guid}/changesets", async (
                Guid claimId,
                InsuranceClaimAggregateService service,
                CancellationToken cancellationToken) =>
            {
                var response = await service.ListChangeSetsAsync(claimId, cancellationToken);
                return Results.Ok(response);
            })
            .WithName("ListClaimChangeSets")
            .WithSummary("Lists committed changesets for an insurance claim.");

        return endpoints;
    }
}
