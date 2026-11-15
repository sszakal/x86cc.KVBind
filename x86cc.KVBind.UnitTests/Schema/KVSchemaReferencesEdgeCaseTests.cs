using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Schema;

namespace x86cc.KVBind.UnitTests.Schema;

// Recursive definitions in less common places: the recursion runs through a field group (so the reference
// index must look inside groups), and two different recursive types share the token "GROUP" (so their
// reference ids must be told apart).
public class KVSchemaReferencesEdgeCaseTests
{
    private static readonly KVSchemaDocument Schema = KVSchemaMapper.Map(BuildDefinition(), "Org");

    [Fact]
    public void ARecursionThroughAFieldGroup_IsIndexedAndResolves()
    {
        var types = KVSchemaReferences.Index(Schema);

        // Units/<id>/Detail(GROUP)/Info/Units/<id>/Detail(GROUP) → a reference back to the outer GROUP.
        var outer = Unit(Schema.Root).NestedNodes.Single(n => n.Key == "Detail").Types.Single();
        var info = outer.Node.Groups.Single(g => g.Key == "Info");
        var inner = Unit(info).NestedNodes.Single(n => n.Key == "Detail").Types.Single();

        outer.Id.Should().NotBeNull();
        inner.Ref.Should().Be(outer.Id);
        types.Should().ContainKey(outer.Id);
        KVSchemaReferences.NodeOf(inner, types).Groups.Single().Fields.Select(f => f.Key).Should().Equal("Code");
    }

    [Fact]
    public void TwoRecursiveTypesSharingAToken_GetDistinctIds()
    {
        var types = KVSchemaReferences.Index(Schema);
        var unit = Unit(Schema.Root);
        var detail = unit.NestedNodes.Single(n => n.Key == "Detail").Types.Single();
        var extra = unit.NestedNodes.Single(n => n.Key == "Extra").Types.Single();

        detail.Token.Should().Be("GROUP");
        extra.Token.Should().Be("GROUP");
        types.Keys.Should().OnlyHaveUniqueItems().And.Contain("GROUP").And.Contain(id => id.StartsWith("GROUP_", StringComparison.Ordinal));

        // The Extra chain refers back to itself, not to the Detail type of the same name.
        var next = extra.Node.NestedNodes.Single(n => n.Key == "Next").Types.Single();
        next.Ref.Should().Be(extra.Id);
        next.Ref.Should().NotBe(detail.Id);
        KVSchemaReferences.NodeOf(next, types).Should().BeSameAs(extra.Node);
    }

    private static KVNodeSchema Unit(KVNodeSchema owner) =>
        owner.Collections.Single(c => c.Key == "Units").ItemTypes.Single().Node;

    private static KVNodeDefinition BuildDefinition()
    {
        var group = new KVBindBuilder<RefGroupDetail>();
        var extra = new KVBindBuilder<RefGroupExtra>();
        extra.NestedNode(x => x.Next, next => next.Bind<RefGroupExtra>("GROUP", extra.SelfReference));
        extra.Build();

        void Unit(KVBindBuilder<RefUnit> unit)
        {
            unit.Field(x => x.Name);
            unit.NestedNode(x => x.Detail, detail => detail.Bind<RefGroupDetail>("GROUP", group.SelfReference));
            unit.NestedNode(x => x.Extra, more => more.Bind<RefGroupExtra>("GROUP", extra.SelfReference));
        }

        group.FieldGroup(x => x.Info, info =>
        {
            info.Field(x => x.Code);
            info.Collection(x => x.Units, units => units.Item<RefUnit>("UNIT", Unit));
        });
        group.Build();

        var root = new KVBindBuilder<RefRoot>();
        root.Collection(x => x.Units, units => units.Item<RefUnit>("UNIT", Unit));
        return root.Build();
    }
}

public partial class RefRoot : KVRootNode
{
    [KVBind("Units")]
    public KVCollectionNode<RefUnit> Units { get; } = new();
}

public partial class RefUnit : KVCollectionItemNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }

    [KVBind("Detail")]
    public partial RefDetail? Detail { get; private set; }

    [KVBind("Extra")]
    public partial RefExtra? Extra { get; private set; }
}

public abstract partial class RefDetail : KVNestedNode;

public partial class RefGroupDetail : RefDetail
{
    [KVBind("Info")]
    public RefInfo Info { get; } = new();
}

public partial class RefInfo : KVFieldGroupNode
{
    [KVBind("Code")]
    public partial string? Code { get; set; }

    [KVBind("Units")]
    public KVCollectionNode<RefUnit> Units { get; } = new();
}

public abstract partial class RefExtra : KVNestedNode;

public partial class RefGroupExtra : RefExtra
{
    [KVBind("Next")]
    public partial RefExtra? Next { get; private set; }
}
