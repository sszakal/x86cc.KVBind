using System.Linq;
using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Definitions;

namespace x86cc.KVBind.UnitTests.Core;

// Exercises the no-lambda builder overloads that pull a node type's definition from its static
// IKVNodeDefinition.Definition (compile-time enforced by the interface constraint).
public class SelfDescribingNodeTests : KVModelTestBase
{
    [Fact]
    public void FieldGroup_no_lambda_pulls_static_definition_and_binds()
    {
        RegisterModelDefinition<SelfDescribingRoot>(b =>
        {
            b.FieldGroup(x => x.Group);
            b.Collection(x => x.Items);
        });

        var definition = CreateRegistry().Get<SelfDescribingRoot>();
        var group = definition.Nodes.Single(n => n.SubSegmentPath == "Group");
        group.Fields.Select(f => f.SubSegmentPath).Should().Contain("Code");

        var root = CreateRoot<SelfDescribingRoot>();
        root.Group.Code = "abc";
        root.Group.Code.Should().Be("abc");
    }

    [Fact]
    public void FieldGroup_no_lambda_with_options_applies_options_on_top()
    {
        RegisterModelDefinition<SelfDescribingRoot>(b =>
        {
            b.FieldGroup(x => x.Group, o => o.DisplayName("Group Section").Annotate("main", true));
            b.Collection(x => x.Items);
        });

        var group = CreateRegistry().Get<SelfDescribingRoot>().Nodes.Single(n => n.SubSegmentPath == "Group");
        group.DisplayName.Should().Be("Group Section");
        group.Annotations.Should().ContainKey("main");
        group.Fields.Select(f => f.SubSegmentPath).Should().Contain("Code"); // structure still pulled from the static def
    }

    [Fact]
    public void Collection_no_lambda_registers_single_item_and_round_trips()
    {
        RegisterModelDefinition<SelfDescribingRoot>(b =>
        {
            b.FieldGroup(x => x.Group);
            b.Collection(x => x.Items);
        });

        var collection = CreateRegistry().Get<SelfDescribingRoot>().Collections.Single(c => c.SubSegmentPath == "Items");
        collection.ItemDefinitionsByToken.Should().ContainKey("SelfItem");
        collection.ItemDefinitionsByToken["SelfItem"].NodeDefinition.Fields
            .Select(f => f.SubSegmentPath).Should().Contain("Name");

        var root = CreateRoot<SelfDescribingRoot>();
        var item = root.Items.Create();
        item.Name = "widget";
        item.Name.Should().Be("widget");
    }

    [Fact]
    public void Collection_Item_no_lambda_inside_configure_pulls_static_definition()
    {
        RegisterModelDefinition<SelfDescribingRoot>(b =>
        {
            b.FieldGroup(x => x.Group);
            b.Collection(x => x.Items, c => c.Item<SelfItem>());
        });

        var collection = CreateRegistry().Get<SelfDescribingRoot>().Collections.Single(c => c.SubSegmentPath == "Items");
        collection.ItemDefinitionsByToken["SelfItem"].NodeDefinition.Fields
            .Select(f => f.SubSegmentPath).Should().Contain("Name");
    }

    [Fact]
    public void NestedNode_Bind_no_lambda_pulls_static_definition()
    {
        RegisterModelDefinition<SelfDescribingRoot>(b =>
        {
            b.FieldGroup(x => x.Group);
            b.Collection(x => x.Items);
            b.NestedNode(x => x.Detail, n => n.Bind<SelfDetail>());
        });

        var nested = CreateRegistry().Get<SelfDescribingRoot>().NestedNodes.Single(n => n.SubSegmentPath == "Detail");
        nested.TypeDefinitionsByToken.Should().ContainKey("SelfDetail");
        nested.TypeDefinitionsByToken["SelfDetail"].NodeDefinition.Fields
            .Select(f => f.SubSegmentPath).Should().Contain("Note");
    }
}

public partial class SelfDescribingRoot : KVRootNode
{
    [KVBind(nameof(Group))]
    public SelfGroup Group { get; } = new();

    [KVBind(nameof(Items))]
    public KVCollectionNode<SelfItem> Items { get; } = new();

    [KVBind(nameof(Detail))]
    public partial SelfDetailBase? Detail { get; private set; }
}

public partial class SelfGroup : KVFieldGroupNode, IKVNodeDefinition
{
    [KVBind(nameof(Code))]
    public partial string? Code { get; set; }

    public static KVNodeDefinition Definition { get; } = Build();

    private static KVNodeDefinition Build()
    {
        var b = new KVBindBuilder<SelfGroup>();
        b.Field(x => x.Code);
        return b.Build();
    }
}

public partial class SelfItem : KVCollectionItemNode, IKVNodeDefinition
{
    [KVBind(nameof(Name))]
    public partial string? Name { get; set; }

    public static KVNodeDefinition Definition { get; } = Build();

    private static KVNodeDefinition Build()
    {
        var b = new KVBindBuilder<SelfItem>();
        b.Field(x => x.Name);
        return b.Build();
    }
}

public abstract partial class SelfDetailBase : KVNestedNode;

public partial class SelfDetail : SelfDetailBase, IKVNodeDefinition
{
    [KVBind(nameof(Note))]
    public partial string? Note { get; set; }

    public static KVNodeDefinition Definition { get; } = Build();

    private static KVNodeDefinition Build()
    {
        var b = new KVBindBuilder<SelfDetail>();
        b.Field(x => x.Note);
        return b.Build();
    }
}
