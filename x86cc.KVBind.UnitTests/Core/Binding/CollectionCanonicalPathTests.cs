using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class CollectionCanonicalPathTests : KVModelTestBase
{
    public CollectionCanonicalPathTests()
    {
        this.RegisterModelDefinition<PathRootNode>(builder =>
            builder.Collection(x => x.Items, options => options.Item<PathItemNode>(item => item.Field(x => x.Name))));
    }

    [Fact]
    public void GetCanonicalPath_WhenCollectionIsBound_ReturnsItsDataPath()
    {
        var root = this.CreateRoot<PathRootNode>();

        root.Items.GetCanonicalPath().Should().Be("Items");
    }

    [Fact]
    public void GetCanonicalPath_WhenCollectionIsUnbound_ReturnsEmptyPath()
    {
        var unbound = new PathCollectionNode();

        unbound.GetCanonicalPath().Should().BeEmpty();
    }

    private sealed class PathRootNode : KVRootNode
    {
        public PathCollectionNode Items { get; } = new();
    }

    private sealed class PathCollectionNode : KVCollectionNode<PathItemNode>;

    private sealed class PathItemNode : KVCollectionItemNode
    {
        public string? Name
        {
            get => this.GetField<string?>("Name");
            set => this.SetField("Name", value);
        }
    }
}
