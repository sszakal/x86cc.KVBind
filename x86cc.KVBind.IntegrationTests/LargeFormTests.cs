using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.LargeForms;
using x86cc.KVBind.Schema;

namespace x86cc.KVBind.IntegrationTests;

// The generated large forms (Phase 2): every size builds, describes its fields, and takes a full fill.
public sealed class LargeFormTests
{
    private static readonly AggregateCatalog Catalog = SampleSetup.CreateCatalog(out _);

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public void EverySize_DescribesItsSectionFields(int size)
    {
        var model = Catalog.Find(LargeFormModels.NameOf(size))!;
        var root = model.Schema.Document.Root;

        root.Groups.Should().HaveCount(size / 25);
        root.Groups.Sum(group => group.Fields.Count).Should().Be(size);
        root.Groups.SelectMany(g => g.Fields).Select(f => f.Kind).Distinct()
            .Should().Contain([KVSchemaValueKind.String, KVSchemaValueKind.Integer, KVSchemaValueKind.Decimal, KVSchemaValueKind.Boolean, KVSchemaValueKind.Date]);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    public void AFullyFilledForm_WithLines_ValidatesCommitsAndReadsBack(int size)
    {
        var model = Catalog.Find(LargeFormModels.NameOf(size))!;
        var overlay = KVOverlay.Create(model.Definition.NewSnapshot(), "tester");
        var root = model.BindNew(overlay);

        var operations = LargeFormFiller.FillAll(model.Schema.Document).ToList();
        operations.AddRange(LargeFormFiller.AddLines(50));
        root.Patch(operations);

        root.Validate().Errors.Should().BeEmpty();
        var snapshot = overlay.Snapshot.Clone();
        snapshot.Apply(root.CreateCommit(DateTimeOffset.UtcNow));

        var data = AggregateDataProjector.Project(KVOverlay.Create(snapshot, "reader"), model.Definition);
        data["LineCount"]!.GetValue<int>().Should().Be(50);
        data["LinesTotal"]!.GetValue<decimal>().Should().Be(Enumerable.Range(1, 50).Sum(i => i * 10m));
        data["S001"]!.AsObject().Count.Should().Be(25);
    }
}
