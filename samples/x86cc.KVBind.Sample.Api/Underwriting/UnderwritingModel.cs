using x86cc.KVBind.Core.Definitions;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api.Aggregates;

namespace x86cc.KVBind.Sample.Api.Underwriting;

/// <summary>Registers the underwriting application with the generic API as <c>/api/models/underwriting</c>.</summary>
public static class UnderwritingModel
{
    public const string Name = "underwriting";

    public static IAggregateModel Create(IKVDefinitionRegistry registry) => new AggregateModel<UnderwritingApplication>(
        Name,
        "Underwriting Applications",
        registry,
        createFields: ["Basics/ApplicationNumber", "Basics/ApplicantName"],
        summaryFields: ["Basics/ApplicationNumber", "Basics/ApplicantName", nameof(UnderwritingApplication.Status), nameof(UnderwritingApplication.LineOfBusiness), nameof(UnderwritingApplication.TotalInsuredValue)],
        description: "Every KVBind feature in one form: typed scalars, allowed values, value rules, profiles, resettable groups, polymorphic and seeded collections, custom operations, nested nodes, reactions, domain events, migrations and inheritance.",
        profileName: application => application.IsSubmitted ? "submit" : "default",
        computed: application => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Premium"] = application.Rating.Premium,
        },
        supportsChildren: true,
        legacySnapshot: LegacySnapshot,
        derivedPaths:
        [
            nameof(UnderwritingApplication.TotalInsuredValue),
            nameof(UnderwritingApplication.LocationCount),
            nameof(UnderwritingApplication.LastRecalculation),
            "Rating/Premium",
        ]);

    /// <summary>
    /// An application as an old release stored it (schema version 0): the applicant under its former key
    /// <c>Basics/InsuredName</c>, an <c>INDIVIDUAL</c> insured with <c>Name</c>, a <c>LegacyScore</c> the
    /// model no longer has, and the number in <c>LegacyReference</c>. The v1–v3 migrations turn it into
    /// today's layout when it is first read (or by the batch migration).
    /// </summary>
    public static KVSnapshot LegacySnapshot(string user)
    {
        var suffix = Random.Shared.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = new KVSnapshot { SchemaVersion = 0, CreatedBy = user, ModifiedBy = user };
        snapshot.Data["Status"] = new KVValue<string>("draft");
        snapshot.Data["LineOfBusiness"] = new KVValue<LineOfBusiness>(LineOfBusiness.Property);
        snapshot.Data["Basics/InsuredName"] = new KVValue<string>($"Legacy Holdings {suffix}");
        snapshot.Data["Basics/IsRenewal"] = new KVValue<bool>(true);
        snapshot.Data["Insured/$type"] = new KVValue<string>("INDIVIDUAL");
        snapshot.Data["Insured/Name"] = new KVValue<string>($"Pat Legacy {suffix}");
        snapshot.Data["LegacyScore"] = new KVValue<int>(42);
        snapshot.Data["LegacyReference"] = new KVValue<string>(suffix);
        return snapshot;
    }
}
