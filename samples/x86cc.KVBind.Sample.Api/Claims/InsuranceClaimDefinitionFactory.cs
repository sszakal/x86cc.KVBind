using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api.Claims;

public sealed class InsuranceClaimDefinitionFactory
{
    private readonly Lazy<KVNodeDefinition> _definition = new(BuildDefinition);

    public KVNodeDefinition Definition => _definition.Value;

    public static readonly IReadOnlyList<string> StatusValues = ["draft", "in_review", "approved", "rejected", "closed"];
    public static readonly IReadOnlyList<string> PriorityValues = ["low", "medium", "high", "critical"];
    public static readonly IReadOnlyList<string> CoverageTypeValues = ["comprehensive", "collision", "liability", "medical"];
    public static readonly IReadOnlyList<string> DamageCategories = ["structural", "electrical", "mechanical", "cosmetic", "water", "fire", "theft", "other"];

    private static KVNodeDefinition BuildDefinition()
    {
        var builder = new KVBindBuilder<InsuranceClaim>();

        builder.Field(x => x.ClaimNumber);
        builder.Field(x => x.Status, f => f.AllowedValues([.. StatusValues]));
        builder.Field(x => x.Priority, f => f.AllowedValues([.. PriorityValues]));
        builder.Field(x => x.IncidentDate);
        builder.Field(x => x.Description);
        builder.Field(x => x.ClaimedTotal);

        builder.FieldGroup(x => x.Policy, policy =>
        {
            policy.Field(x => x.PolicyNumber);
            policy.Field(x => x.CoverageType, f => f.AllowedValues([.. CoverageTypeValues]));
        });

        builder.Collection(x => x.DamagedItems, damagedItems =>
        {
            damagedItems.Item<DamagedItem>(item =>
            {
                item.Field(x => x.Description);
                item.Field(x => x.Category, f => f.AllowedValues([.. DamageCategories]));
                item.Field(x => x.EstimatedAmount);
            });
        });

        builder.Collection(x => x.Notes, notes =>
        {
            notes.Item<ClaimNote>(note => note.Field(x => x.Text));
        });

        builder.NestedNode(x => x.Claimant, claimant =>
        {
            claimant.Bind<PersonClaimant>("PERSON", person => person.Field(x => x.FullName));
            claimant.Bind<CompanyClaimant>("COMPANY", company => company.Field(x => x.CompanyName));
        });

        builder.OnChange(path => path.Collection(x => x.DamagedItems).Any(), x => x.RecalculateClaimedTotal);
        builder.OnChange(path => path.Collection(x => x.DamagedItems).Field(x => x.EstimatedAmount), x => x.RecalculateClaimedTotal);

        return builder.Build();
    }
}
