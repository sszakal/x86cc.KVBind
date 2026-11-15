using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Schema;
using x86cc.KVBind.UnitTests.Core.Binding;

namespace x86cc.KVBind.UnitTests.Schema;

// A recursive definition (part → Detail: ASSEMBLY → Parts → part …) maps to a finite schema: the first
// ASSEMBLY carries an id, and the ASSEMBLY inside it refers back to that id.
public class KVSchemaReferencesTests
{
    private static readonly KVSchemaDocument Schema = KVSchemaMapper.Map(RecursiveCollectionTreeTests.Definition, "Tree");

    [Fact]
    public void RecursiveType_FirstOccurrenceCarriesAnId_InnerOccurrenceARef()
    {
        var outer = Detail(Part(Schema.Root)).Types.Single(type => type.Token == "ASSEMBLY");
        var inner = Detail(Part(outer.Node)).Types.Single(type => type.Token == "ASSEMBLY");

        outer.Id.Should().Be("ASSEMBLY");
        outer.Ref.Should().BeNull();
        outer.Node.Fields.Select(field => field.Key).Should().Equal("Subtotal");
        inner.Ref.Should().Be("ASSEMBLY");
        inner.Id.Should().BeNull();
        inner.Node.Fields.Should().BeEmpty();
    }

    [Fact]
    public void NonRecursiveTypes_CarryNeitherIdNorRef()
    {
        var leaf = Detail(Part(Schema.Root)).Types.Single(type => type.Token == "LEAF");
        var part = Schema.Root.Collections.Single().ItemTypes.Single();

        leaf.Id.Should().BeNull();
        leaf.Ref.Should().BeNull();
        part.Id.Should().BeNull();
        part.Ref.Should().BeNull();
    }

    [Fact]
    public void NodeOf_FollowsTheRef_SoAPathResolvesAtAnyDepth()
    {
        var types = KVSchemaReferences.Index(Schema);
        var node = Schema.Root;

        // Parts/<id>/Detail(ASSEMBLY)/Parts/<id>/Detail(ASSEMBLY)/… five levels down.
        for (var level = 0; level < 5; level++)
        {
            var assembly = Detail(Part(node)).Types.Single(type => type.Token == "ASSEMBLY");
            node = KVSchemaReferences.NodeOf(assembly, types);
        }

        node.Fields.Select(field => field.Key).Should().Equal("Subtotal");
        Part(node).Fields.Select(field => field.Key).Should().Equal("Name", "Cost");
    }

    [Fact]
    public void NodeOf_UnknownRef_Throws()
    {
        var dangling = new KVItemTypeSchema { Token = "X", Ref = "missing" };

        var act = () => KVSchemaReferences.NodeOf(dangling, KVSchemaReferences.Index(Schema));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'missing'*");
    }

    [Fact]
    public void RecursiveSchema_RoundTripsThroughJson()
    {
        var json = JsonSerializer.Serialize(Schema);
        var copy = JsonSerializer.Deserialize<KVSchemaDocument>(json)!;

        var types = KVSchemaReferences.Index(copy);
        types.Keys.Should().Equal("ASSEMBLY");
        JsonSerializer.Serialize(copy).Should().Be(json);
    }

    private static KVNodeSchema Part(KVNodeSchema owner) => owner.Collections.Single(c => c.Key == "Parts").ItemTypes.Single().Node;

    private static KVNestedNodeSchema Detail(KVNodeSchema part) => part.NestedNodes.Single(n => n.Key == "Detail");
}
