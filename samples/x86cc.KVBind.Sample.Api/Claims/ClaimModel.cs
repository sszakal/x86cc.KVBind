using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Sample.Api.Aggregates;

namespace x86cc.KVBind.Sample.Api.Claims;

/// <summary>Registers the insurance claim with the generic API as <c>/api/models/claims</c> (alias <c>/api/claims</c>).</summary>
public static class ClaimModel
{
    public const string Name = "claims";

    public static IAggregateModel Create(IKVDefinitionRegistry registry) => new AggregateModel<InsuranceClaim>(
        Name,
        "Insurance Claims",
        registry,
        createFields: [nameof(InsuranceClaim.ClaimNumber)],
        summaryFields: [nameof(InsuranceClaim.ClaimNumber), nameof(InsuranceClaim.Status), nameof(InsuranceClaim.Priority), nameof(InsuranceClaim.Description)],
        description: "The simple example: a claim with a policy group, damaged items, notes and a polymorphic claimant.",
        profileName: claim => claim.UsesSubmitProfile ? "submit" : "draft",
        computed: claim => new Dictionary<string, object?>(StringComparer.Ordinal) { ["ClaimedTotal"] = claim.ClaimedTotal });
}
