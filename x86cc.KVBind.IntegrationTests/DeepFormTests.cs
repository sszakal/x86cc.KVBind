using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.DeepForms;

namespace x86cc.KVBind.IntegrationTests;

// The recursive component tree (`deep`): it builds at every depth, costs roll up through every level, and
// the sample's schema lookups resolve paths at any depth through the schema's references.
public sealed class DeepFormTests
{
    private static readonly AggregateCatalog Catalog = SampleSetup.CreateCatalog(out _);
    private static readonly IAggregateModel Model = Catalog.Find(DeepFormModels.Name)!;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ATreeOfAboutAThousandFields_BuildsValidatesAndRollsUp(int depth)
    {
        var shape = DeepFormShape.AtDepth(depth);
        var (root, overlay) = Build(shape);

        root.Validate().Errors.Should().BeEmpty();
        root.TotalCost.Should().Be(DeepFormFiller.ExpectedTotal(shape));
        root.ComponentCount.Should().Be(shape.TopLevel);
        DeepForm.CountAll(root.Components).Should().Be(shape.Components);
        DeepForm.DepthOf(root.Components).Should().Be(depth);

        var stored = overlay.Changes.Keys.Count(key => !key.Contains('$', StringComparison.Ordinal));
        stored.Should().Be(shape.FieldValues);
        shape.FieldValues.Should().BeInRange(950, 1100);
    }

    [Fact]
    public void ALeafChange_RipplesUpEveryAssemblyToTheRoot()
    {
        var shape = DeepFormShape.AtDepth(4);
        var (root, _) = Build(shape);
        var leaf = DeepFormFiller.DeepestLeaf(shape);

        root.Patch(KVPatchOperation.Set($"{leaf.Path}/Detail/Price", 1010m));

        root.TotalCost.Should().Be(DeepFormFiller.ExpectedTotal(shape) + 1000m);
    }

    [Fact]
    public void DroppingAnAssembly_RemovesItsSubtreeFromTheTotal()
    {
        var shape = DeepFormShape.AtDepth(3);
        var (root, _) = Build(shape);
        var before = root.TotalCost;

        // c1 is a level-1 assembly: quantity 1 × unit cost 2 plus its two subtrees.
        root.Patch(KVPatchOperation.Drop("/Components/c1/Detail"));

        root.TotalCost.Should().Be(before - (DeepFormFiller.ExpectedTotal(shape) / shape.TopLevel) + 2m);
        DeepForm.CountAll(root.Components).Should().Be(shape.Components - 6);
    }

    [Fact]
    public void Commit_ThenRebind_ProjectsTheWholeTree()
    {
        var shape = DeepFormShape.AtDepth(4);
        var (root, overlay) = Build(shape);
        var snapshot = overlay.Snapshot.Clone();
        snapshot.Apply(root.CreateCommit(DateTimeOffset.UtcNow));

        var data = AggregateDataProjector.Project(KVOverlay.Create(snapshot, "reader"), Model.Definition);

        var level = data["Components"]!.AsArray()[0]!;
        for (var i = 1; i < 4; i++)
        {
            level["$type"]!.GetValue<string>().Should().Be("COMPONENT");
            level = level["Detail"]!["Components"]!.AsArray()[0]!;
        }

        level["$id"]!.GetValue<string>().Should().Be("c1-1-1-1");
        level["Detail"]!["$type"]!.GetValue<string>().Should().Be("PURCHASED");
        level["Measurements"]!.AsArray().Should().HaveCount(shape.Measurements);
    }

    [Theory]
    [InlineData("Components/c1/Name", "Components/Name", "Components/c1/Name")]
    [InlineData("Components/c1/Detail/Components/c1-2/Detail/Components/c1-2-1/Detail/Price", "Components/Detail/Components/Detail/Components/Detail/Price", "Components/c1/Detail/Components/c1-2/Detail/Components/c1-2-1/Detail/Price")]
    [InlineData("Components/c1/Detail/Components/c1-1/Detail/Components/c1-1-1/Measurements/m2/Unit", "Components/Detail/Components/Detail/Components/Measurements/Unit", "Components/c1/Detail/Components/c1-1/Detail/Components/c1-1-1/Measurements/m2/Unit")]
    public void SchemaLookups_ResolveAtAnyDepth(string path, string schemaPath, string displayPath)
    {
        Model.Schema.ToSchemaPath(path).Should().Be(schemaPath);
        Model.Schema.ToDisplayPath(path).Should().Be(displayPath);
        Model.Schema.IsField(path).Should().BeTrue();
    }

    [Fact]
    public void ValueLabels_ResolveDeepInTheTree()
    {
        Model.Schema.LabelValue("Components/c1/Detail/Components/c1-1/Detail/Components/c1-1-1/Category", "electrical").Should().Be("Electrical");
        Model.Schema.LabelValue("Components/c1/Detail/Components/c1-1/Tags", new[] { "spare" }).Should().BeEquivalentTo(new[] { "Spare" });
        Model.Schema.IsField("Components/c1/Detail/Components").Should().BeFalse();
    }

    [Fact]
    public void Schema_IsFinite_WithTheAssemblyReferencedFromInsideItself()
    {
        var component = Model.Schema.Document.Root.Collections.Single(c => c.Key == "Components").ItemTypes.Single();
        var assembly = component.Node.NestedNodes.Single().Types.Single(t => t.Token == "ASSEMBLY");
        var innerComponent = assembly.Node.Collections.Single().ItemTypes.Single();
        var innerAssembly = innerComponent.Node.NestedNodes.Single().Types.Single(t => t.Token == "ASSEMBLY");

        assembly.Id.Should().Be("ASSEMBLY");
        innerAssembly.Ref.Should().Be("ASSEMBLY");
    }

    private static (DeepForm Root, KVOverlay Overlay) Build(DeepFormShape shape)
    {
        var overlay = KVOverlay.Create(Model.Definition.NewSnapshot(), "tester");
        var root = (DeepForm)Model.BindNew(overlay);
        root.Title = "Tree";
        root.Patch(DeepFormFiller.Build(shape));
        return (root, overlay);
    }
}
