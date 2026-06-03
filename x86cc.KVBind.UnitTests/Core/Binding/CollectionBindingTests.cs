using AwesomeAssertions;
using Meziantou.Framework.InlineSnapshotTesting;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class CollectionBindingTests : KVModelTestBase
{
    public CollectionBindingTests()
    {
        RegisterModelDefinition<CollectionTestModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.CollectionKey1, collection =>
            {
                collection.Item<FieldGroupNode>(item =>
                {
                    item.Field(x => x.Field1);
                    item.Field(x => x.Field2);
                });
            });

            modelBuilder.Collection(x => x.CollectionKey2, collection =>
            {
                collection.Item<FieldGroupNode>(item =>
                {
                    item.Field(x => x.Field1);
                    item.Field(x => x.Field2);
                });
            });
        });

        RegisterModelDefinition<PolymorphicCollectionTestModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Item<FieldGroupNode>(item =>
                {
                    item.Field(x => x.Field1);
                    item.Field(x => x.Field2);
                });
                collection.Item<SpecialFieldGroupNode>("special", item =>
                {
                    item.Field(x => x.Field1);
                    item.Field(x => x.Field2);
                    item.Field(x => x.SubtypeNotes);
                });
            });
        });

        RegisterModelDefinition<MissingCollectionDeclarationModel>(_ =>
        {
        });

        RegisterModelDefinition<DefaultItemIdCollectionTestModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Items, collection =>
            {
                collection.Item<FieldGroupNode>(item => item.Field(x => x.Field1));
            });
        });

        RegisterModelDefinition<NestedCollectionTestModel>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Level1Collection, collection =>
            {
                collection.Item<NodeItemWithCollection>(item =>
                {
                    item.Collection(x => x.Level2Collection, collection =>
                    {
                        collection.Item<FieldGroupNode>(item =>
                        {
                            item.Field(x => x.Field1);
                            item.Field(x => x.Field2);
                        });
                    });
                });
            });
        });

    }

    [Fact]
    public void CollectionBinding_WhenItemsAreCreated_StoresItemFieldsAtCanonicalPaths()
    {
        var data = new KVModelRoot();
        var model = CreateRoot<CollectionTestModel>(data);

        var firstItem = model.CollectionKey1.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        firstItem.Field1 = "A1";
        firstItem.Field2 = "A2";

        var secondItem = model.CollectionKey2.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        secondItem.Field1 = "B1";
        secondItem.Field2 = "B2";

        var firstId = model.CollectionKey1.GetItemId(firstItem);
        var secondId = model.CollectionKey2.GetItemId(secondItem);

        model.CollectionKey1.Count().Should().Be(1);
        model.CollectionKey2.Count().Should().Be(1);
        model.CollectionKey1.GetById(firstId)!.Field1.Should().Be("A1");
        model.CollectionKey1.GetById(firstId)!.Field2.Should().Be("A2");
        model.CollectionKey2.GetById(secondId)!.Field1.Should().Be("B1");
        model.CollectionKey2.GetById(secondId)!.Field2.Should().Be("B2");
        
        data.Overlay.Changes.Should().ContainKey("CollectionKey1/11111111-1111-1111-1111-111111111111/Field1").WhoseValue.Should().Be("A1");
        data.Overlay.Changes.Should().ContainKey("CollectionKey1/11111111-1111-1111-1111-111111111111/Field2").WhoseValue.Should().Be("A2");
        data.Overlay.Changes.Should().ContainKey("CollectionKey2/22222222-2222-2222-2222-222222222222/Field1").WhoseValue.Should().Be("B1");
        data.Overlay.Changes.Should().ContainKey("CollectionKey2/22222222-2222-2222-2222-222222222222/Field2").WhoseValue.Should().Be("B2");
    }

    [Fact]
    public void CollectionBinding_WhenItemIsRemoved_RemovesItemFromRuntimeCollection()
    {
        var data = new KVModelRoot();
        var model = CreateRoot<CollectionTestModel>(data);

        var item1 = model.CollectionKey1.Create();
        item1.Field1 = "keep";
        var item2 = model.CollectionKey1.Create();
        item2.Field1 = "remove";

        var keepId = model.CollectionKey1.GetItemId(item1);
        var removeId = model.CollectionKey1.GetItemId(item2);

        model.CollectionKey1.RemoveById(removeId).Should().BeTrue();

        model.CollectionKey1.Count().Should().Be(1);
        model.CollectionKey1.GetById(keepId).Should().NotBeNull();
        model.CollectionKey1.GetById(removeId).Should().BeNull();
    }

    [Fact]
    public void CollectionBinding_WhenItemIsMoved_ReordersRuntimeCollection()
    {
        var data = new KVModelRoot();
        var model = CreateRoot<CollectionTestModel>(data);

        var first = model.CollectionKey1.Create();
        var second = model.CollectionKey1.Create();
        var third = model.CollectionKey1.Create();

        var id1 = model.CollectionKey1.GetItemId(first);
        var id2 = model.CollectionKey1.GetItemId(second);
        var id3 = model.CollectionKey1.GetItemId(third);

        model.CollectionKey1.MoveById(id3, 0).Should().BeTrue();

        model.CollectionKey1.GetItemId(model.CollectionKey1.ElementAt(0)).Should().Be(id3);
        model.CollectionKey1.GetItemId(model.CollectionKey1.ElementAt(1)).Should().Be(id1);
        model.CollectionKey1.GetItemId(model.CollectionKey1.ElementAt(2)).Should().Be(id2);
    }

    [Fact]
    public void CollectionBinding_WhenItemExists_ReturnsBoundItemById()
    {
        var data = new KVModelRoot();
        var model = CreateRoot<CollectionTestModel>(data);

        var created = model.CollectionKey1.Create();
        created.Field1 = "lookup";
        var id = model.CollectionKey1.GetItemId(created);

        var loaded = model.CollectionKey1.GetById(id);

        loaded.Should().NotBeNull();
        loaded!.Field1.Should().Be("lookup");
    }

    [Fact]
    public void CollectionBinding_WhenAllowedSubtypeIsCreated_StoresTypeTokenAndHydratesConcreteSubtype()
    {
        var data = new KVModelRoot();
        var model = CreateRoot<PolymorphicCollectionTestModel>(data);

        var subtype = model.Items.Create<SpecialFieldGroupNode>();
        subtype.Field1 = "Subtype1";
        subtype.Field2 = "Subtype2";
        subtype.SubtypeNotes = "SubtypeNotes";

        var id = model.Items.GetItemId(subtype);

        var hydrated = model.Items.GetById(id);
        hydrated.Should().BeOfType<SpecialFieldGroupNode>();
        hydrated!.Field1.Should().Be("Subtype1");
        hydrated.Field2.Should().Be("Subtype2");
        hydrated.As<SpecialFieldGroupNode>().SubtypeNotes.Should().Be("SubtypeNotes");
    }

    [Fact]
    public void CollectionBinding_WhenSubtypeIsNotAllowed_ThrowsOnCreate()
    {
        var data = new KVModelRoot();
        var model = CreateRoot<CollectionTestModel>(data);

        var act = () => model.CollectionKey1.Create<SpecialFieldGroupNode>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CollectionBinding_WhenStoredTypeTokenIsUnknown_ThrowsOnBind()
    {
        var data = new KVModelRoot();
        const string id = "777";
        data.Set($"Items/$items", new string[] { id });
        data.Set($"Items/{id}/$type", "unknown");
        data.Set($"Items/{id}/Field1", "value");

        var act = () => CreateRoot<PolymorphicCollectionTestModel>(data);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CollectionDefinition_WhenSubtypeTokenIsDuplicated_ThrowsOnBuild()
    {
        var act = () =>
        {
            var builder = new KVBindBuilder<InvalidPolymorphicCollectionTestModel>();
            builder.Collection(x => x.Items, collection =>
            {
                collection.Item<SpecialFieldGroupNode>("same_token", item =>
                {
                    item.Field(x => x.Field1);
                    item.Field(x => x.Field2);
                    item.Field(x => x.SubtypeNotes);
                });
                collection.Item<AnotherSpecialFieldGroupNode>("same_token", item =>
                {
                    item.Field(x => x.Field1);
                    item.Field(x => x.Field2);
                    item.Field(x => x.SubtypeRank);
                });
            });
        };

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CollectionBinding_WhenCollectionIsNotDeclared_ThrowsOnUsage()
    {
        var model = CreateRoot<MissingCollectionDeclarationModel>();
        var act = () => model.Items.Create();

        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void CollectionBinding_WhenItemIsCreatedWithoutId_UsesGuidStringId()
    {
        var model = CreateRoot<DefaultItemIdCollectionTestModel>();

        var item = model.Items.Create();
        var id = model.Items.GetItemId(item);

        Guid.TryParse(id, out _).Should().BeTrue();
    }

    [Fact]
    public void CollectionBinding_WhenCollectionsAreNested_StoresNestedItemFieldsAtCanonicalPaths()
    {
        var model = new KVModelRoot();
        var rootNode = CreateRoot<NestedCollectionTestModel>(model);

        var level1FirstItem = rootNode.Level1Collection.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var level1SecondItem = rootNode.Level1Collection.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"));

        var level1FirstItemLevel2FirstItem = level1FirstItem.Level2Collection.Create(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        level1FirstItemLevel2FirstItem.Field1 = "1";
        level1FirstItemLevel2FirstItem.Field2 = "2";
        var level1FirstItemLevel2SecondItem = level1FirstItem.Level2Collection.Create(Guid.Parse("44444444-4444-4444-4444-444444444444"));
        level1FirstItemLevel2SecondItem.Field1 = "3";
        level1FirstItemLevel2SecondItem.Field2 = "4";
        
        var level2FirstItemLevel2FirstItem = level1SecondItem.Level2Collection.Create(Guid.Parse("55555555-5555-5555-5555-555555555555"));
        level2FirstItemLevel2FirstItem.Field1 = "5";
        level2FirstItemLevel2FirstItem.Field2 = "6";
        var level2FirstItemLevel2SecondItem = level1SecondItem.Level2Collection.Create(Guid.Parse("66666666-6666-6666-6666-666666666666"));
        level2FirstItemLevel2SecondItem.Field1 = "7";
        level2FirstItemLevel2SecondItem.Field2 = "8";
     
        
        model.Overlay.Changes.Should().ContainKey("Level1Collection/11111111-1111-1111-1111-111111111111/Level2Collection/33333333-3333-3333-3333-333333333333/Field1").WhoseValue.Should().Be("1");
        model.Overlay.Changes.Should().ContainKey("Level1Collection/11111111-1111-1111-1111-111111111111/Level2Collection/44444444-4444-4444-4444-444444444444/Field2").WhoseValue.Should().Be("4");
        model.Overlay.Changes.Should().ContainKey("Level1Collection/22222222-2222-2222-2222-222222222222/Level2Collection/55555555-5555-5555-5555-555555555555/Field1").WhoseValue.Should().Be("5");
        model.Overlay.Changes.Should().ContainKey("Level1Collection/22222222-2222-2222-2222-222222222222/Level2Collection/66666666-6666-6666-6666-666666666666/Field2").WhoseValue.Should().Be("8");
        
        
        InlineSnapshot.Validate(rootNode, """
            Level1Collection:
              - Level2Collection:
                  - Field1: 1
                    Field2: 2
                  - Field1: 3
                    Field2: 4
              - Level2Collection:
                  - Field1: 5
                    Field2: 6
                  - Field1: 7
                    Field2: 8
            Id:
            Version:
            """);
    }

}

public partial class CollectionTestModel : KVRootNode
{
    [KVBind("CollectionKey1")]
    public KVCollectionNode<FieldGroupNode> CollectionKey1 { get; set; } = new();

    [KVBind("CollectionKey2")]
    public KVCollectionNode<FieldGroupNode> CollectionKey2 { get; set; } = new();
}

public partial class PolymorphicCollectionTestModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<FieldGroupNode> Items { get; set; } = new();
}

public partial class InvalidPolymorphicCollectionTestModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<FieldGroupNode> Items { get; set; } = new();
}

public partial class FieldGroupNode : KVCollectionItemNode
{
    [KVBind("Field1")]
    public partial string Field1 { get; set; }
    
    [KVBind("Field2")]
    public partial string Field2 { get; set; }
}

public partial class SpecialFieldGroupNode : FieldGroupNode
{
    [KVBind("SubtypeNotes")]
    public partial string SubtypeNotes { get; set; }
}

public partial class AnotherSpecialFieldGroupNode : FieldGroupNode
{
    [KVBind("SubtypeRank")]
    public partial int SubtypeRank { get; set; }
}

public partial class MissingCollectionDeclarationModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<FieldGroupNode> Items { get; set; } = new();
}

public partial class DefaultItemIdCollectionTestModel : KVRootNode
{
    [KVBind("Items")]
    public KVCollectionNode<FieldGroupNode> Items { get; set; } = new();
}

public partial class NestedCollectionTestModel : KVRootNode
{
    [KVBind(nameof(Level1Collection))]
    public KVCollectionNode<NodeItemWithCollection> Level1Collection { get; set; } = new();
}

public partial class NodeItemWithCollection : KVCollectionItemNode
{
    [KVBind(nameof(Level2Collection))]
    public KVCollectionNode<FieldGroupNode> Level2Collection { get; set; } = new();
}
