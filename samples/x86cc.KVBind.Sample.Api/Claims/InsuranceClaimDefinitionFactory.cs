using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api.Claims;

public sealed class InsuranceClaimDefinitionFactory
{
    private readonly Lazy<KVNodeDefinition> _definition = new(BuildDefinition);

    public KVNodeDefinition Definition => _definition.Value;

    private static KVNodeDefinition BuildDefinition()
    {
        var builder = new KVBindBuilder<InsuranceClaim>();

        builder.Field(x => x.ClaimNumber);
        builder.Field(x => x.Status);
        builder.Field(x => x.IncidentDate);
        builder.Field(x => x.Description);
        builder.Field(x => x.ClaimedTotal);

        builder.FieldGroup(x => x.Policy, policy =>
        {
            policy.Field(x => x.PolicyNumber);
            policy.Field(x => x.CoverageType);
        });

        builder.Collection(x => x.DamagedItems, damagedItems =>
        {
            damagedItems.Item<DamagedItem>(item =>
            {
                item.Field(x => x.Description);
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

        return builder.Build();
    }
}
