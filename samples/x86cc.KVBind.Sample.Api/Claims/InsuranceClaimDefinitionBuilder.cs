using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.Sample.Api.Claims;

public sealed class InsuranceClaimDefinitionBuilder : IKVModelDefinitionBuilder
{
    public Type ModelType => typeof(InsuranceClaim);

    public static readonly IReadOnlyList<string> DamageCategories =
        ["structural", "electrical", "mechanical", "cosmetic", "water", "fire", "theft", "other"];

    public static readonly IReadOnlyList<(string Id, string Label)> ClaimTags =
    [
        ("urgent",       "Urgent"),
        ("high_value",   "High Value"),
        ("repeat_claim", "Repeat Claim"),
        ("fraud_risk",   "Fraud Risk"),
        ("expedite",     "Expedite"),
        ("complex",      "Complex"),
    ];

    public KVNodeDefinition Build()
    {
        var builder = new KVBindBuilder<InsuranceClaim>();

        // ── ClaimNumber ───────────────────────────────────────────────────────────
        builder.Field(x => x.ClaimNumber, f =>
            f.DisplayName("Claim Number")
             .Validation(p => p.For<SubmitClaimValidationProfile>(r => r.Required())));

        // ── Status — AllowedValue with labels + required for non-draft ────────────
        builder.Field(x => x.Status, f =>
        {
            f.DisplayName("Status").UiControl("select");
            f.Default("draft"); // a fresh claim starts in draft (materialized by CreateNew/ApplyDefaults)
            // AllowedValue(storedToken, id, label)
            f.AllowedValue("draft",      "draft",      "Draft");
            f.AllowedValue("in_review",  "in_review",  "In Review");
            f.AllowedValue("approved",   "approved",   "Approved");
            f.AllowedValue("rejected",   "rejected",   "Rejected");
            f.AllowedValue("closed",     "closed",     "Closed");
            f.Validation(p => p.For<SubmitClaimValidationProfile>(r => r.Required()));
        });

        // ── Priority — AllowedValue with labels ───────────────────────────────────
        builder.Field(x => x.Priority, f =>
        {
            f.DisplayName("Priority").UiControl("radio");
            f.AllowedValue("low",      "low",      "Low");
            f.AllowedValue("medium",   "medium",   "Medium");
            f.AllowedValue("high",     "high",     "High");
            f.AllowedValue("critical", "critical", "Critical");
        });

        builder.Field(x => x.IncidentDate, f => f.DisplayName("Incident Date").UiControl("date"));

        builder.Field(x => x.Description, f =>
            f.DisplayName("Description")
             .UiControl("textarea")
             .Validation(p => p.For<SubmitClaimValidationProfile>(r => r.MaxLength(1000))));


        // ── Tags — AllowedElementValue for multi-select array ─────────────────────
        builder.Field(x => x.Tags, f =>
        {
            f.DisplayName("Tags").UiControl("multiselect");
            foreach (var (id, label) in ClaimTags)
                f.AllowedElementValue<string>(id, id, label);
        });

        // ── Policy (field group) ──────────────────────────────────────────────────
        builder.FieldGroup(x => x.Policy, policy =>
        {
            policy.Field(x => x.PolicyNumber, f => f.DisplayName("Policy Number"));

            // Coverage type — two options use AllowedValueComponent with a template
            // to tell the UI "this option requires additional parameters".
            policy.Field(x => x.CoverageType, f =>
            {
                f.DisplayName("Coverage Type").UiControl("select");
                f.AllowedValue("comprehensive",  "comprehensive",  "Comprehensive");
                f.AllowedValueComponent("collision",      "collision",      "Collision",
                    c => c.Template("Collision — {Deductible:C} deductible")
                           .Placeholder<decimal>("Deductible", "Deductible Amount"));
                f.AllowedValueComponent("collision_plus", "collision_plus", "Collision Plus",
                    c => c.Template("Collision Plus — {Deductible:C} deductible, {ExcessAmount:C} excess")
                           .Placeholder<decimal>("Deductible",    "Deductible Amount")
                           .Placeholder<decimal>("ExcessAmount",  "Excess Amount"));
                f.AllowedValue("liability", "liability", "Liability Only");
                f.AllowedValue("medical",   "medical",   "Medical Payments");
                f.Validation(p => p.For<SubmitClaimValidationProfile>(r => r.Required()));
            });
        }, options => options.Section());

        // ── Damaged Items ─────────────────────────────────────────────────────────
        builder.Collection(x => x.DamagedItems, items =>
        {
            items.DisplayName("Damaged Items");
            items.Item<DamagedItem>(item =>
            {
                item.Field(x => x.Description, f =>
                    f.DisplayName("Description")
                     .Validation(p => p.For<SubmitClaimValidationProfile>(r => r.Required())));
                item.Field(x => x.Category, f =>
                {
                    f.DisplayName("Category").UiControl("select");
                    foreach (var cat in DamageCategories)
                        f.AllowedValue(cat, cat, ToLabel(cat));
                });
                item.Field(x => x.EstimatedAmount, f => f.DisplayName("Estimated Amount").UiControl("number"));
            });

            // Only enforce a minimum when submitting — draft can have zero items.
            items.Validation(p => p.For<SubmitClaimValidationProfile>(r => r.MinCount(1)));
        });

        // ── Notes ─────────────────────────────────────────────────────────────────
        builder.Collection(x => x.Notes, notes =>
        {
            notes.DisplayName("Notes");
            notes.Item<ClaimNote>(note => note.Field(x => x.Text, f => f.DisplayName("Note text")));
        });

        // ── Claimant ──────────────────────────────────────────────────────────────
        builder.NestedNode(x => x.Claimant, claimant =>
        {
            claimant.DisplayName("Claimant");
            claimant.Bind<PersonClaimant>("PERSON", person =>
                person.Field(x => x.FullName,
                    f => f.Validation(p => p.For<SubmitClaimValidationProfile>(r => r.Required()))));
            claimant.Bind<CompanyClaimant>("COMPANY", company =>
                company.Field(x => x.CompanyName,
                    f => f.Validation(p => p.For<SubmitClaimValidationProfile>(r => r.Required()))));
        });


        return builder.Build();
    }

    internal static string ToLabel(string snake) =>
        string.Concat(snake.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}
