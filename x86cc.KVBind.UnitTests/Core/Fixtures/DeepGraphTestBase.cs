using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public abstract class DeepGraphTestBase : KVModelTestBase
{
    protected static string DeepLeafPath => $"Level1Collection/{TestIds.Level1Text}/Level2Collection/{TestIds.Level2Text}/Level3Collection/{TestIds.Level3Text}/Level4Collection/{TestIds.Level4Text}";
    protected static string Level1Path => $"Level1Collection/{TestIds.Level1Text}";
    protected static string Level2Path => $"{Level1Path}/Level2Collection/{TestIds.Level2Text}";
    protected static string Level3Path => $"{Level2Path}/Level3Collection/{TestIds.Level3Text}";

    protected DeepGraphTestBase()
    {
        RegisterModelDefinition<DeepNestedCollectionRoot>(modelBuilder =>
        {
            modelBuilder.Collection(x => x.Level1Collection, level1 =>
            {
                level1.Item<DeepLevel1Item>(level1Item =>
                {
                    level1Item.Collection(x => x.Level2Collection, level2 =>
                    {
                        level2.Item<DeepLevel2Item>(level2Item =>
                        {
                            level2Item.Collection(x => x.Level3Collection, level3 =>
                            {
                                level3.Item<DeepLevel3Item>(level3Item =>
                                {
                                    level3Item.Collection(x => x.Level4Collection, level4 =>
                                    {
                                        level4.Item<DeepLevel4Item>(level4Item =>
                                        {
                                            level4Item.Field(x => x.LeafField);
                                            level4Item.NestedNode(x => x.Animal, animal =>
                                            {
                                                animal.Bind<DeepDogNode>("DOG", dog => dog.Field(x => x.DogName, options => options.Required()));
                                                animal.Bind<DeepCatNode>("CAT", cat => cat.Field(x => x.CatName));
                                            });
                                        });
                                    });
                                });
                            });
                        });
                    });
                });
            });
        });
    }

    protected TRoot CommitAndContinue<TRoot>(TRoot root, KVModelRoot model, ICollection<KVCommit> commits)
        where TRoot : KVRootNode
    {
        var commit = root.CreateCommit(DateTimeOffset.UtcNow);
        commits.Add(commit);
        model.Snapshot.Apply(commit);
        model.ReplaceOverlay(KVOverlay.Create(model.Snapshot, model.Overlay.User));
        return BindRoot(root, model);
    }

    protected static string CreateDeepLeaf(DeepNestedCollectionRoot root, out DeepLevel4Item leaf)
    {
        var level1 = root.Level1Collection.Create(TestIds.Level1);
        var level2 = level1.Level2Collection.Create(TestIds.Level2);
        var level3 = level2.Level3Collection.Create(TestIds.Level3);
        leaf = level3.Level4Collection.Create(TestIds.Level4);

        return DeepLeafPath;
    }

    protected static DeepLevel4Item GetDeepLeaf(DeepNestedCollectionRoot root)
    {
        return GetLevel3(root).Level4Collection.GetById(TestIds.Level4Text)
               ?? throw new InvalidOperationException("Deep leaf item was not found.");
    }

    protected static DeepLevel3Item GetLevel3(DeepNestedCollectionRoot root)
    {
        var level1 = root.Level1Collection.GetById(TestIds.Level1Text)
                     ?? throw new InvalidOperationException("Level 1 item was not found.");
        var level2 = level1.Level2Collection.GetById(TestIds.Level2Text)
                     ?? throw new InvalidOperationException("Level 2 item was not found.");
        return level2.Level3Collection.GetById(TestIds.Level3Text)
               ?? throw new InvalidOperationException("Level 3 item was not found.");
    }
}

public partial class DeepNestedCollectionRoot : KVRootNode
{
    [KVBind(nameof(Level1Collection))]
    public KVCollectionNode<DeepLevel1Item> Level1Collection { get; } = new();
}

public partial class DeepLevel1Item : KVCollectionItemNode
{
    [KVBind(nameof(Level2Collection))]
    public KVCollectionNode<DeepLevel2Item> Level2Collection { get; } = new();
}

public partial class DeepLevel2Item : KVCollectionItemNode
{
    [KVBind(nameof(Level3Collection))]
    public KVCollectionNode<DeepLevel3Item> Level3Collection { get; } = new();
}

public partial class DeepLevel3Item : KVCollectionItemNode
{
    [KVBind(nameof(Level4Collection))]
    public KVCollectionNode<DeepLevel4Item> Level4Collection { get; } = new();
}

public partial class DeepLevel4Item : KVCollectionItemNode
{
    [KVBind(nameof(LeafField))]
    public partial string LeafField { get; set; }

    [KVBind(nameof(Animal))]
    public partial DeepAnimalNode? Animal { get; private set; }
}

public abstract partial class DeepAnimalNode : KVNestedNode;

public partial class DeepDogNode : DeepAnimalNode
{
    [KVBind(nameof(DogName))]
    public partial string DogName { get; set; }
}

public partial class DeepCatNode : DeepAnimalNode
{
    [KVBind(nameof(CatName))]
    public partial string CatName { get; set; }
}
