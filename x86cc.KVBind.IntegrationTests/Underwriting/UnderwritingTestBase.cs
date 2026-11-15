using System.Text.Json;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api.Underwriting;

namespace x86cc.KVBind.IntegrationTests.Underwriting;

/// <summary>
/// Model-level scenarios for the underwriting application (no database): the aggregate is created and
/// patched the way the API does it, with patch values as JSON elements, as they arrive from a client.
/// </summary>
public abstract class UnderwritingTestBase
{
    protected static readonly KVNodeDefinition Definition = new UnderwritingDefinitionBuilder().Build();

    /// <summary>A brand-new application (defaults and seeds applied), like POST /api/models/underwriting.</summary>
    protected static UnderwritingApplication CreateNew(string number = "UW-1001")
    {
        var application = KVRootNode.CreateNew<UnderwritingApplication>(KVOverlay.Create(Definition.NewSnapshot(), "tester"), Definition);
        application.Basics.ApplicationNumber = number;
        return application;
    }

    /// <summary>Commits the application and binds the result again, as a later request would.</summary>
    protected static UnderwritingApplication CommitAndReload(UnderwritingApplication application)
    {
        var snapshot = application.RootModel().Overlay.Snapshot.Clone();
        snapshot.Apply(application.CreateCommit(DateTimeOffset.UtcNow));
        return Bind(snapshot);
    }

    protected static UnderwritingApplication Bind(KVSnapshot snapshot) =>
        KVRootNode.Create<UnderwritingApplication>(KVOverlay.Create(snapshot.Clone(), "tester"), Definition);

    protected static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    protected static KVPatchOperation Set(string path, string json) => KVPatchOperation.Set(path, Json(json));

    protected static KVPatchOperation Op(string operation, string path, string? json = null) =>
        KVPatchOperation.Custom(operation, path, json is null ? null : Json(json));

    protected static IReadOnlyList<(string Path, string Code)> Errors(UnderwritingApplication application) =>
        application.Validate().Errors.Select(error => (error.Path, error.Code)).ToArray();

    /// <summary>Fills what the submit profile requires, so a test can submit and change one thing.</summary>
    protected static void MakeSubmittable(UnderwritingApplication application)
    {
        application.Patch(
            Set("/Basics/ApplicantName", "\"Acme Ltd\""),
            Set("/Basics/EffectiveDate", "\"2027-01-01\""),
            Set("/Basics/AnnualRevenue", "5000000"),
            Set("/Contact/Notes", "\"Long-standing customer, clean history.\""),
            Set("/Insured/CompanyName", "\"Acme Ltd\""),
            KVPatchOperation.Add("/Locations", new KVAddPatchPayload("hq", "BUILDING")),
            Set("/Locations/hq/Name", "\"Head office\""),
            Set("/Locations/hq/Value", "1000000"),
            KVPatchOperation.Add("/Contacts", new KVAddPatchPayload("c1")),
            Set("/Contacts/c1/Name", "\"Jo Owner\""));
        foreach (var (id, _) in UnderwritingDefinitionBuilder.StandardQuestions)
        {
            application.Patch(Set($"/Questions/{id}/Answer", "false"));
        }
    }
}
