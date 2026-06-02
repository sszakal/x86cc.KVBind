using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class RootBindingTests : KVModelTestBase
{
    public RootBindingTests()
    {
        RegisterModelDefinition<NewRootNode>(builder =>
        {
            builder.Field(x => x.Title);
            builder.FieldGroup(x => x.Child, child => child.Field(x => x.Name));
            builder.Collection(x => x.Items, collection =>
            {
                collection.Item<NewCollectionItemNode>(item => item.Field(x => x.Value));
                collection.Item<NewSpecialCollectionItemNode>("special", item =>
                {
                    item.Field(x => x.Value);
                    item.Field(x => x.Special);
                });
            });
        });
    }
    
    [Fact]
    public void RootBinding_WhenRootModelIsBound_ExposesRootModelIdAndVersion()
    {
        var model = new KVModelRoot { Id = "contract-1", Version = "v1" };
        var root = CreateRoot<NewRootNode>(model);

        root.RootModel().Should().BeSameAs(model);
        root.Id.Should().Be("contract-1");
        root.Version.Should().Be("v1");

        root.Id = "contract-2";
        root.Version = "v2";

        model.Id.Should().Be("contract-2");
        model.Version.Should().Be("v2");
    }

    [Fact]
    public void RootBinding_WhenRootIsUnbound_ThrowsActionableError()
    {
        var root = new NewRootNode();

        var act = () => _ = root.RootModel();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RootBinding_WhenRootUsesPlainModel_Throws()
    {
        var root = new NewRootNode();
        var definition = CreateRegistry().Get<NewRootNode>();

        var act = () => root.BindPlainModelForTest(new KVModel(), definition);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RootBinding_WhenCollectionIsDeclared_BindsCollectionToChildModel()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewRootNode>(model);

        model.ChildModels.Should().ContainKey("Items");
        root.Items.Model.Should().BeSameAs(model.ChildModels["Items"]);
    }

    [Fact]
    public void RootBinding_WhenFieldsAreSet_UsesLocalKeysAndStoresResolvedPaths()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewRootNode>(model);

        root.Title = "Contract A";
        root.Child.Name = "Artist";

        root.Title.Should().Be("Contract A");
        root.Child.Name.Should().Be("Artist");

        model.Get<string>("Title").Should().Be("Contract A");
        model.Get<string>("Child/Name").Should().Be("Artist");
    }

    [Fact]
    public void RootBinding_WhenFieldIsNotDefined_ThrowsOnRead()
    {
        var root = CreateRoot<NewRootNode>();

        var act = () => root.ReadNotDefined();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RootBinding_WhenCollectionItemIsCreated_BindsItemAndStoresMetadata()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewRootNode>(model);

        var item = root.Items.Create();
        item.Value = "A";
        var id = root.Items.GetItemId(item);

        root.Items.Count().Should().Be(1);
        root.Items.GetById(id)!.Value.Should().Be("A");
    }

    [Fact]
    public void RootBinding_WhenCollectionItemIsMovedAndRemoved_UpdatesRuntimeChildren()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewRootNode>(model);
        var first = root.Items.Create();
        var second = root.Items.Create();
        var id1 = root.Items.GetItemId(first);
        var id2 = root.Items.GetItemId(second);

        root.Items.MoveById(id2, 0).Should().BeTrue();
        root.Items.RemoveById(id1).Should().BeTrue();

        root.Items.Count().Should().Be(1);
        root.Items.GetById(id2).Should().NotBeNull();
        root.Items.GetById(id1).Should().BeNull();
    }

    [Fact]
    public void RootBinding_WhenCollectionSubtypeIsCreated_StoresTokenAndHydratesSubtype()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewRootNode>(model);

        var item = root.Items.Create<NewSpecialCollectionItemNode>();
        item.Value = "S";
        item.Special = "Subtype";
        var id = root.Items.GetItemId(item);

        var hydrated = root.Items.GetById(id);
        hydrated.Should().BeOfType<NewSpecialCollectionItemNode>();
        hydrated!.As<NewSpecialCollectionItemNode>().Special.Should().Be("Subtype");
    }

    [Fact]
    public void RootBinding_WhenCollectionSubtypeIsNotAllowed_ThrowsOnCreate()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<NewRootNode>(model);

        var act = () => root.Items.Create<NewUnregisteredCollectionItemNode>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RootBinding_WhenCollectionStoredTypeTokenIsUnknown_ThrowsOnBind()
    {
        var model = new KVModelRoot();
        const string id = "unknown-id";
        var collectionModel = model.EnsureCollectionModel("Items");
        var itemModel = collectionModel.EnsureItemModel(id);
        itemModel.Set("$type", "unknown");
        itemModel.Set("Items/unknown-id/Value", "V");
        
        var act = () => CreateRoot<NewRootNode>(model);

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class NewRootNode : KVRootNode
    {
        public NewChildNode Child { get; } = new();

        public NewCollectionNode Items { get; } = new();

        public string? Title
        {
            get => GetField<string?>("Title");
            set => SetField("Title", value);
        }

        public string? ReadNotDefined() => GetField<string?>("NotDefined");

        public void BindPlainModelForTest(KVModel model, KVNodeDefinition definition) => Bind(model, definition);

    }

    private sealed class NewChildNode : KVFieldGroupNode
    {
        public string? Name
        {
            get => GetField<string?>("Name");
            set => SetField("Name", value);
        }

    }

    private sealed class NewCollectionNode : KVCollectionNode<NewCollectionItemNode>;

    private class NewCollectionItemNode : KVCollectionItemNode
    {
        public string? Value
        {
            get => GetField<string?>("Value");
            set => SetField("Value", value);
        }
    }

    private sealed class NewSpecialCollectionItemNode : NewCollectionItemNode
    {
        public string? Special
        {
            get => GetField<string?>("Special");
            set => SetField("Special", value);
        }
    }

    private sealed class NewUnregisteredCollectionItemNode : NewCollectionItemNode;
}
