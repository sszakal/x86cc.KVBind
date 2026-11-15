using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Core.Migrations;

namespace x86cc.KVBind.Sample.Api.Underwriting;

/// <summary>
/// The underwriting application's definition: one section per KVBind feature area (see the comments
/// 1a–1n, which match the scenario tests in x86cc.KVBind.IntegrationTests/Underwriting). The UI renders
/// all of it from the generated schema, so every feature here is visible and editable in the sample app.
/// </summary>
public sealed class UnderwritingDefinitionBuilder : IKVModelDefinitionBuilder
{
    public const int SchemaVersion = 3;

    public static readonly IReadOnlyList<(string Id, string Label)> Industries =
    [
        ("manufacturing", "Manufacturing"),
        ("retail", "Retail"),
        ("hospitality", "Hospitality"),
        ("construction", "Construction"),
        ("technology", "Technology"),
        ("logistics", "Logistics"),
    ];

    public static readonly IReadOnlyList<(string Id, string Prompt)> StandardQuestions =
    [
        ("PRIOR_LOSSES", "Any losses in the last five years?"),
        ("BANKRUPTCY", "Any bankruptcy or insolvency in the last ten years?"),
        ("HAZMAT", "Are hazardous materials stored on site?"),
    ];

    public Type ModelType => typeof(UnderwritingApplication);

    public KVNodeDefinition Build()
    {
        var builder = new KVBindBuilder<UnderwritingApplication>();

        // ── Root: lifecycle (1g PatchReadOnly + 1h operations), classification, derived values (1j) ──
        builder.Field(x => x.Status, f => f
            .DisplayName("Status")
            .Default("draft")
            .PatchReadOnly() // only the SUBMIT / QUOTE / BIND / DECLINE operations change it
            .AllowedValue("draft", "draft", "Draft")
            .AllowedValue("submitted", "submitted", "Submitted")
            .AllowedValue("quoted", "quoted", "Quoted")
            .AllowedValue("bound", "bound", "Bound")
            .AllowedValue("declined", "declined", "Declined"));
        builder.Field(x => x.LineOfBusiness, f => f.DisplayName("Line of Business").Default(LineOfBusiness.Property).UiControl("radio"));
        builder.Field(x => x.Tags, f =>
        {
            f.DisplayName("Tags");
            f.AllowedElementValue<string>("priority", "priority", "Priority");
            f.AllowedElementValue<string>("referral", "referral", "Referral");
            f.AllowedElementValue<string>("large_risk", "large_risk", "Large Risk");
            f.AllowedElementValue<string>("renewal_review", "renewal_review", "Renewal Review");
        });
        builder.Field(x => x.DeclineReason, f => f.DisplayName("Decline Reason").PatchReadOnly());
        builder.Field(x => x.TotalInsuredValue, f => f.DisplayName("Total Insured Value").PatchReadOnly().Help("Sum of the locations' values, kept by a change reaction."));
        builder.Field(x => x.LocationCount, f => f.DisplayName("Location Count").PatchReadOnly());
        builder.Field(x => x.LastRecalculation, f => f.DisplayName("Last Recalculation").PatchReadOnly().Help("The KVChangeKind the totals reaction last saw."));

        // ── 1a: every scalar kind, with defaults that apply only when an application is born ──
        builder.FieldGroup(
            x => x.Basics,
            basics =>
            {
                basics.Field(x => x.ApplicationNumber, f => f
                    .DisplayName("Application Number")
                    .Required()
                    .Pattern("UW-[0-9]{4,8}")
                    .Help("Format UW-1234"));
                basics.Field(x => x.ApplicantName, f => f.DisplayName("Applicant Name").MaxLength(120));
                basics.Field(x => x.YearsInBusiness, f => f.DisplayName("Years in Business").Range(0, 200));
                basics.Field(x => x.AnnualRevenue, f => f.DisplayName("Annual Revenue").Min(0m));
                basics.Field(x => x.IsRenewal, f => f.DisplayName("Renewal").Default(false));
                basics.Field(x => x.EffectiveDate, f => f.DisplayName("Effective Date"));
                basics.Field(x => x.ReceivedAt, f => f.DisplayName("Received At"));
                basics.Field(x => x.PreferredCallTime, f => f.DisplayName("Preferred Call Time"));
                basics.Field(x => x.ReviewSla, f => f.DisplayName("Review SLA").Default(TimeSpan.FromDays(5)));
                basics.Field(x => x.BrokerReference, f => f.DisplayName("Broker Reference"));
                basics.Field(x => x.Channel, f => f.DisplayName("Sales Channel").Default(SalesChannel.Broker));
                basics.Field(x => x.Coverages, f => f.DisplayName("Coverages").Default(CoverageOptions.Fire | CoverageOptions.Theft));

                // 1d: a group rule of the submit profile.
                basics.Validation((KVGroupValidationProfileBuilder<ApplicationBasics> profiles) => profiles
                    .For<SubmitApplicationProfile>(rules => rules
                        .Required(x => x.ApplicantName)
                        .Required(x => x.EffectiveDate)
                        .Required(x => x.AnnualRevenue)));
            },
            options => options.DisplayName("Basics").Section());

        // ── 1b: allowed values — labelled, plain, numeric, components with placeholders, element-wise ──
        builder.FieldGroup(
            x => x.Classification,
            classification =>
            {
                classification.Field(x => x.Industry, f =>
                {
                    f.DisplayName("Industry");
                    foreach (var (id, label) in Industries)
                    {
                        f.AllowedValue(id, id, label);
                    }
                });
                classification.Field(x => x.RiskTier, f => f.DisplayName("Risk Tier").AllowedValues("A", "B", "C").UiControl("radio"));
                classification.Field(x => x.Territory, f => f.DisplayName("Territory").AllowedValues(1, 2, 3, 4, 5));
                classification.Field(x => x.DeductibleOption, f => f
                    .DisplayName("Deductible")
                    .AllowedValue("standard", "standard", "Standard")
                    .AllowedValueComponent("custom", "custom", "Custom amount", c => c
                        .Template("Custom — {Amount:C} per occurrence")
                        .Placeholder<decimal>("Amount", "Deductible Amount"))
                    .AllowedValueComponent("split", "split", "Split", c => c
                        .Template("Split — {Property:C} property, {Liability:C} liability")
                        .Placeholder<decimal>("Property", "Property Deductible")
                        .Placeholder<decimal>("Liability", "Liability Deductible")));
                classification.Field(x => x.Exclusions, f =>
                {
                    f.DisplayName("Exclusions");
                    f.AllowedElementValue<string>("war", "war", "War & Terrorism");
                    f.AllowedElementValue<string>("nuclear", "nuclear", "Nuclear");
                    f.AllowedElementValue<string>("cyber", "cyber", "Cyber");
                    f.AllowedElementValue<string>("pandemic", "pandemic", "Pandemic");
                });
            },
            options => options.DisplayName("Classification"));

        // ── 1c: value rules on fields (always) and in the submit profile ──
        builder.FieldGroup(
            x => x.Contact,
            contact =>
            {
                contact.Field(x => x.Email, f => f.DisplayName("Email").MaxLength(80).Pattern("[^@\\s]+@[^@\\s]+\\.[a-z]{2,}"));
                contact.Field(x => x.Phone, f => f.DisplayName("Phone").Pattern("\\+?[0-9 ]{7,15}"));
                contact.Field(x => x.Website, f => f.DisplayName("Website").MinLength(5).MaxLength(100));
                contact.Field(x => x.Employees, f => f.DisplayName("Employees").Range(1, 100_000));
                contact.Field(x => x.DiscountPercent, f => f.DisplayName("Discount %").Range(0m, 25m));
                contact.Field(x => x.InceptionDate, f => f.DisplayName("Inception Date").Max(new DateOnly(2030, 12, 31)));
                contact.Field(x => x.Notes, f => f
                    .DisplayName("Notes")
                    .UiControl("textarea")
                    .MaxLength(2000)
                    .Validation(p => p.For<SubmitApplicationProfile>(r => r.Required().MinLength(10))));
            },
            options => options.DisplayName("Contact"));

        // ── 1e: a resettable group with defaults and a nested group ──
        builder.FieldGroup(
            x => x.Premises,
            premises =>
            {
                premises.Field(x => x.Construction, f => f
                    .DisplayName("Construction")
                    .Default("masonry")
                    .AllowedValue("frame", "frame", "Frame")
                    .AllowedValue("masonry", "masonry", "Masonry")
                    .AllowedValue("steel", "steel", "Steel")
                    .AllowedValue("fire_resistive", "fire_resistive", "Fire Resistive"));
                premises.Field(x => x.Floors, f => f.DisplayName("Floors").Default(1).Range(1, 200));
                premises.Field(x => x.YearBuilt, f => f.DisplayName("Year Built").Range(1800, 2100));
                premises.FieldGroup(
                    x => x.Protection,
                    protection =>
                    {
                        protection.Field(x => x.Sprinklered, f => f.DisplayName("Sprinklered").Default(false));
                        protection.Field(x => x.AlarmType, f => f
                            .DisplayName("Alarm")
                            .AllowedValue("none", "none", "None")
                            .AllowedValue("local", "local", "Local")
                            .AllowedValue("central", "central", "Central Station"));
                        protection.Field(x => x.FireStationDistanceKm, f => f.DisplayName("Fire Station Distance (km)").Min(0m));
                    },
                    options => options.DisplayName("Protection").Section());
            },
            options => options.DisplayName("Premises").Resettable());

        // ── 1n: inherited from the parent (master) application; read-only in a child ──
        builder.FieldGroup(
            x => x.Program,
            program =>
            {
                program.Field(x => x.ProgramName, f => f.DisplayName("Program"));
                program.Field(x => x.Carrier, f => f.DisplayName("Carrier"));
                program.Field(x => x.CommissionPercent, f => f.DisplayName("Commission %").Range(0m, 30m));
            },
            options => options.DisplayName("Program Terms").Inherited().Help("Inherited: a child application reads these from its parent."));

        // ── 1g: written only by the QUOTE operation and the reactions ──
        builder.FieldGroup(
            x => x.Rating,
            rating =>
            {
                rating.Field(x => x.BaseRate, f => f.DisplayName("Base Rate (per 1,000)"));
                rating.Field(x => x.Modifier, f => f.DisplayName("Modifier"));
                rating.Field(x => x.Premium, f => f.DisplayName("Premium"));
                rating.Field(x => x.QuotedAt, f => f.DisplayName("Quoted At"));
            },
            options => options.DisplayName("Rating").PatchReadOnly().Help("Set by the QUOTE operation; the premium follows the insured value."));

        // ── 1f: a polymorphic collection with a collection inside its items, count and aggregate rules ──
        builder.Collection(x => x.Locations, locations =>
        {
            locations.DisplayName("Locations");
            locations.Item<BuildingLocation>("BUILDING", building =>
            {
                ConfigureLocation(building);
                building.Field(x => x.Address, f => f.DisplayName("Address").MaxLength(200));
                building.Field(x => x.Occupancy, f => f
                    .DisplayName("Occupancy")
                    .AllowedValue("office", "office", "Office")
                    .AllowedValue("warehouse", "warehouse", "Warehouse")
                    .AllowedValue("retail", "retail", "Retail"));
                building.Collection(x => x.Tenants, tenants =>
                {
                    tenants.DisplayName("Tenants");
                    tenants.Item<Tenant>("TENANT", tenant =>
                    {
                        tenant.Field(x => x.TenantName, f => f.DisplayName("Tenant").Required().ItemTitle());
                        tenant.Field(x => x.SharePercent, f => f.DisplayName("Share %").Range(0m, 100m));
                    });
                    tenants.MaxCount(10);
                    tenants.AggregateSum("SharePercent").LessThanOrEqual(100m);
                });
            });
            locations.Item<VehicleLocation>("VEHICLE", vehicle =>
            {
                ConfigureLocation(vehicle);
                vehicle.Field(x => x.Vin, f => f.DisplayName("VIN").Pattern("[A-HJ-NPR-Z0-9]{17}"));
                vehicle.Field(x => x.ModelYear, f => f.DisplayName("Model Year").Range(1950, 2100));
            });
            locations.MaxCount(25);
            locations.AggregateSum("Value").LessThanOrEqual(50_000_000m);
            locations.Validation(p => p.For<SubmitApplicationProfile>(r => r.MinCount(1)));
        });

        // Seeded questions: born with the application, membership and order locked (AllowOperations() with
        // nothing). Answers stay editable; the PatchReadOnly prompt also rules out RESET of an item.
        builder.Collection(x => x.Questions, questions =>
        {
            questions.DisplayName("Underwriting Questions");
            questions.Item<UnderwritingQuestion>("QUESTION", question =>
            {
                question.Field(x => x.Prompt, f => f.DisplayName("Question").PatchReadOnly().ItemTitle());
                question.Field(x => x.Answer, f => f.DisplayName("Answer"));
                question.Field(x => x.Details, f => f.DisplayName("Details").UiControl("textarea").MaxLength(500));
                question.Validation((KVGroupValidationProfileBuilder<UnderwritingQuestion> profiles) => profiles
                    .For<SubmitApplicationProfile>(rules => rules.Required(x => x.Answer)));
                question.Validation((KVGroupRuleBuilder<UnderwritingQuestion> rules) => rules
                    .When(a => a.Get(x => x.Answer) == true, yes => yes.Required(x => x.Details).MinLength(x => x.Details, 5)));
            });
            foreach (var (id, prompt) in StandardQuestions)
            {
                questions.Seed<UnderwritingQuestion>(id, q => q.Prompt = prompt);
            }

            questions.AllowOperations();
        });

        // Contacts can be added, removed and reset, but not reordered.
        builder.Collection(x => x.Contacts, contacts =>
        {
            contacts.DisplayName("Contacts");
            contacts.Item<ContactPerson>("CONTACT", contact =>
            {
                contact.Field(x => x.Name, f => f.DisplayName("Name").Required().ItemTitle());
                contact.Field(x => x.Role, f => f
                    .DisplayName("Role")
                    .AllowedValue("owner", "owner", "Owner")
                    .AllowedValue("finance", "finance", "Finance")
                    .AllowedValue("risk", "risk", "Risk Manager"));
                contact.Field(x => x.Email, f => f.DisplayName("Email").Pattern("[^@\\s]+@[^@\\s]+\\.[a-z]{2,}"));
            });
            contacts.AllowOperations(KVPatchOperations.Add, KVPatchOperations.Remove, KVPatchOperations.Reset);
            contacts.Validation(p => p.For<SubmitApplicationProfile>(r => r.NotEmpty()));
        });

        // Endorsements are PatchReadOnly: only the collection operation ADD_ENDORSEMENT adds them.
        builder.Collection(x => x.Endorsements, endorsements =>
        {
            endorsements.DisplayName("Endorsements");
            endorsements.Item<Endorsement>("ENDORSEMENT", endorsement =>
            {
                endorsement.Field(x => x.Code, f => f.DisplayName("Code").ItemTitle());
                endorsement.Field(x => x.Description, f => f.DisplayName("Description"));
            });
            endorsements.PatchReadOnly();
            endorsements.Operation<EndorsementRequest>("ADD_ENDORSEMENT", x => x.AddEndorsement);
        });

        // ── 1i: nested nodes — a default subtype, and one required only for marine risks ──
        builder.NestedNode(x => x.Insured, insured => insured
            .DisplayName("Insured")
            .Bind<InsuredPerson>("PERSON", person =>
            {
                person.Field(x => x.FullName, f => f.DisplayName("Full Name").ItemTitle().Validation(p => p.For<SubmitApplicationProfile>(r => r.Required())));
                person.Field(x => x.DateOfBirth, f => f.DisplayName("Date of Birth"));
            })
            .Bind<InsuredCompany>("COMPANY", company =>
            {
                company.Field(x => x.CompanyName, f => f.DisplayName("Company Name").ItemTitle().Validation(p => p.For<SubmitApplicationProfile>(r => r.Required())));
                company.Field(x => x.RegistrationNumber, f => f.DisplayName("Registration Number").Pattern("[A-Z]{2}[0-9]{6}"));
            })
            .DefaultType<InsuredCompany>()
            .Required());
        builder.NestedNode(x => x.Vessel, vessel => vessel
            .DisplayName("Vessel")
            .Bind<Yacht>("YACHT", yacht =>
            {
                yacht.Field(x => x.VesselName, f => f.DisplayName("Vessel Name").ItemTitle());
                yacht.Field(x => x.LengthMeters, f => f.DisplayName("Length (m)").Range(2m, 200m));
            })
            .Bind<CargoShip>("CARGO", cargo =>
            {
                cargo.Field(x => x.VesselName, f => f.DisplayName("Vessel Name").ItemTitle());
                cargo.Field(x => x.Tonnage, f => f.DisplayName("Tonnage").Min(1));
            }));

        // ── 1d: root group rules — conditional on the line of business and the status ──
        builder.Validation((KVGroupRuleBuilder<UnderwritingApplication> rules) => rules
            .When(a => a.Get(x => x.LineOfBusiness) == LineOfBusiness.Marine, marine => marine.Required(x => x.Vessel))
            .When(a => a.Get(x => x.Status) == "declined", declined => declined.Required(x => x.DeclineReason)));

        // ── 1h: custom operations on the root ──
        builder.Operation("SUBMIT", x => x.Submit);
        builder.Operation<QuoteRequest>("QUOTE", x => x.Quote);
        builder.Operation("BIND", x => x.Bind);
        builder.Operation<string>("DECLINE", x => x.Decline);

        // ── 1j: reactions keep the derived totals, for value, added, removed, moved and replaced changes ──
        builder.OnChange(path => path.Collection(x => x.Locations).Field(x => x.Value), x => x.RecalculateTotals);
        builder.OnChange(path => path.Collection(x => x.Locations).Any(), x => x.RecalculateTotals);

        // ── 1m: migrations from the legacy layout (see UnderwritingModel.LegacySnapshot) ──
        builder.Migration(1, m => m
            .RenameField(KVTarget.Root.Seg("Basics").Seg("InsuredName"), "ApplicantName")
            .Backfill(KVTarget.Root.Seg("Basics").Seg("Channel"), _ => SalesChannel.Broker));
        builder.Migration(2, m => m
            .ReplaceNestedType(KVTarget.Root.Seg("Insured").OfType("INDIVIDUAL"), "PERSON", reshape => reshape
                .Rename("Name", "FullName")));
        builder.Migration(SchemaVersion, m => m
            .Remove(KVTarget.Root.Seg("LegacyScore"))
            .Backfill(KVTarget.Root.Seg("Basics").Seg("ApplicationNumber"), field =>
                field.Absolute("LegacyReference") is string legacy ? $"UW-{legacy}" : null)
            .Remove(KVTarget.Root.Seg("LegacyReference")));

        return builder.Build();
    }

    private static void ConfigureLocation<TLocation>(KVBindBuilder<TLocation> location)
        where TLocation : Location
    {
        location.Field(x => x.Name, f => f.DisplayName("Name").Required().ItemTitle());
        location.Field(x => x.Value, f => f.DisplayName("Insured Value").Min(0m));
        location.Field(x => x.Approved, f => f.DisplayName("Approved").PatchReadOnly());
        location.Operation("APPROVE", x => x.Approve);
    }
}
