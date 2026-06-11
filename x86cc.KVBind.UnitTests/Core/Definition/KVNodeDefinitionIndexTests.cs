using System.Linq;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

// Pins the lazy SubSegmentPath indexes on KVNodeDefinition (FindNode/FindCollection/FindNestedNode),
// which mirror the existing FindField index.
public class KVNodeDefinitionIndexTests : KVModelTestBase
{
    [Fact]
    public void FindNode_FindCollection_FindNestedNode_resolve_by_sub_segment_path()
    {
        RegisterModelDefinition<SelfDescribingRoot>(b =>
        {
            b.FieldGroup(x => x.Group);
            b.Collection(x => x.Items);
            b.NestedNode(x => x.Detail, n => n.Bind<SelfDetail>());
        });

        var definition = CreateRegistry().Get<SelfDescribingRoot>();

        definition.FindNode("Group").Should().BeSameAs(definition.Nodes.Single(n => n.SubSegmentPath == "Group"));
        definition.FindCollection("Items").Should().BeSameAs(definition.Collections.Single(c => c.SubSegmentPath == "Items"));
        definition.FindNestedNode("Detail").Should().BeSameAs(definition.NestedNodes.Single(n => n.SubSegmentPath == "Detail"));
    }

    [Fact]
    public void Find_methods_return_null_for_unknown_keys()
    {
        RegisterModelDefinition<SelfDescribingRoot>(b =>
        {
            b.FieldGroup(x => x.Group);
            b.Collection(x => x.Items);
        });

        var definition = CreateRegistry().Get<SelfDescribingRoot>();

        definition.FindNode("missing").Should().BeNull();
        definition.FindCollection("missing").Should().BeNull();
        definition.FindNestedNode("missing").Should().BeNull();
    }
}
