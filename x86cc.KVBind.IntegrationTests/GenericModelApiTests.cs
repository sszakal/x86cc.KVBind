using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.Claims;

namespace x86cc.KVBind.IntegrationTests;

// The generic layer the sample API serves every model through: the generated definition, the data
// projection and the path helpers. No database.
public sealed class GenericModelApiTests
{
    private static readonly AggregateCatalog Catalog = SampleSetup.CreateCatalog(out _);

    private static IAggregateModel Claims => Catalog.Find(ClaimModel.Name)!;

    [Fact]
    public void ClaimDefinition_DescribesTheFormTheClaimUiRendered()
    {
        var root = Claims.Schema.Document.Root;

        root.Fields.Select(f => f.Key).Should().Equal("ClaimNumber", "Status", "Priority", "IncidentDate", "Description", "Tags");
        root.Fields.Single(f => f.Key == "Status").AllowedValues!.Options.Select(o => o.Label)
            .Should().Equal("Draft", "In Review", "Approved", "Rejected", "Closed");
        root.Fields.Single(f => f.Key == "Tags").AllowedValues!.AppliesToElements.Should().BeTrue();

        var policy = root.Groups.Single();
        policy.Annotations.Should().ContainSingle(a => a.Key == UiAnnotations.RoleKey && a.Value == "section");
        var coverage = policy.Fields.Single(f => f.Key == "CoverageType");
        coverage.AllowedValues!.Options.Single(o => o.Id == "collision_plus").Placeholders.Select(p => p.Name)
            .Should().Equal("Deductible", "ExcessAmount");

        root.Collections.Select(c => c.Key).Should().Equal("DamagedItems", "Notes");
        root.NestedNodes.Single().Types.Select(t => t.Token).Should().Equal("PERSON", "COMPANY");
    }

    [Fact]
    public void Catalog_ServesEveryModel_WithItsSchemaVersion()
    {
        Catalog.All.Select(m => m.Name).Should().BeEquivalentTo("claims", "underwriting", "large-100", "large-500", "large-1000", "large-2000", "deep");
        Catalog.Find("UNDERWRITING")!.Definition.CurrentSchemaVersion.Should().Be(3, "model names are case-insensitive in routes");
    }

    [Fact]
    public void DataProjector_ReadsAnyModel_IntoATreeOfFieldsGroupsItemsAndNestedNodes()
    {
        var overlay = KVOverlay.Create(Claims.Definition.NewSnapshot(), "tester");
        var claim = (InsuranceClaim)Claims.BindNew(overlay);
        claim.Patch(
            KVPatchOperation.Set("/ClaimNumber", "C-1"),
            KVPatchOperation.Set("/Policy/CoverageType", "collision"),
            KVPatchOperation.Add("/DamagedItems", new KVAddPatchPayload("i1")),
            KVPatchOperation.Set("/DamagedItems/i1/EstimatedAmount", 10m),
            KVPatchOperation.Init("/Claimant", "PERSON"),
            KVPatchOperation.Set("/Claimant/FullName", "Ann"));

        var data = AggregateDataProjector.Project(overlay, Claims.Definition);

        data["ClaimNumber"]!.GetValue<string>().Should().Be("C-1");
        data["Status"]!.GetValue<string>().Should().Be("draft");
        data["Policy"]!["CoverageType"]!.GetValue<string>().Should().Be("collision");
        var item = data["DamagedItems"]!.AsArray().Single()!;
        item["$id"]!.GetValue<string>().Should().Be("i1");
        item["$type"]!.GetValue<string>().Should().Be("DamagedItem");
        item["EstimatedAmount"]!.GetValue<decimal>().Should().Be(10m);
        data["Claimant"]!["$type"]!.GetValue<string>().Should().Be("PERSON");
        data["Notes"]!.AsArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData("DamagedItems/i1/Category", "DamagedItems/Category", "Damaged Items/i1/Category")]
    [InlineData("Policy/CoverageType", "Policy/CoverageType", "Policy/Coverage Type")]
    [InlineData("Claimant/$type", "Claimant", "Claimant")]
    [InlineData("DamagedItems/$items", "DamagedItems", "Damaged Items")]
    public void SchemaPaths_DropItemIdsByPosition_AndDisplayPathsUseLabels(string path, string schemaPath, string displayPath)
    {
        Claims.Schema.ToSchemaPath(path).Should().Be(schemaPath);
        Claims.Schema.ToDisplayPath(path).Should().Be(displayPath);
    }

    [Fact]
    public void LabelValue_MapsStoredTokensToTheirLabels()
    {
        Claims.Schema.LabelValue("Status", "in_review").Should().Be("In Review");
        Claims.Schema.LabelValue("Tags", new[] { "urgent", "unknown" }).Should().BeEquivalentTo(new[] { "Urgent", "unknown" });
        Claims.Schema.LabelValue("DamagedItems/x/Category", "fire").Should().Be("Fire");
    }
}
