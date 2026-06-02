using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class DeepGraphPatchTests : DeepGraphTestBase
{
    [Fact]
    public void DeepGraphPatch_WhenOperationsCreateDeepGraph_AppliesSequentiallyInOneRequest()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);

        var result = root.Patch(
            KVPatchOperation.Add("/Level1Collection", new KVAddPatchPayload(TestIds.Level1)),
            KVPatchOperation.Add($"/{Level1Path}/Level2Collection", new KVAddPatchPayload(TestIds.Level2)),
            KVPatchOperation.Add($"/{Level2Path}/Level3Collection", new KVAddPatchPayload(TestIds.Level3)),
            KVPatchOperation.Add($"/{Level3Path}/Level4Collection", new KVAddPatchPayload(TestIds.Level4)),
            KVPatchOperation.Set($"/{DeepLeafPath}/LeafField", "leaf-from-patch"),
            KVPatchOperation.Init($"/{DeepLeafPath}/Animal", "DOG"),
            KVPatchOperation.Set($"/{DeepLeafPath}/Animal/DogName", "Rex"));

        var leaf = GetDeepLeaf(root);
        leaf.LeafField.Should().Be("leaf-from-patch");
        leaf.Animal.Should().BeOfType<DeepDogNode>().Subject.DogName.Should().Be("Rex");
        result.Changes.Should().Contain(change => change.Path == Level1Path && change.ChangeType == KVChangeDeltaType.Added);
    }

    [Fact]
    public void DeepGraphPatch_WhenMiddleCollectionItemIsRemoved_RemovesDescendantSubtree()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        CreateDeepLeaf(root, out var leaf);
        leaf.LeafField = "base";
        root.Patch(KVPatchOperation.Init($"/{DeepLeafPath}/Animal", "DOG"));
        root.Patch(KVPatchOperation.Set($"/{DeepLeafPath}/Animal/DogName", "Rex"));
        CommitAndContinue(root, model, new List<KVCommit>());

        root.Patch(KVPatchOperation.Remove($"/{Level2Path}"));

        var level1 = root.Level1Collection.GetById(TestIds.Level1Text);
        level1.Should().NotBeNull();
        level1!.Level2Collection.GetById(TestIds.Level2Text).Should().BeNull();
        root.GetAllChanges().Changes.Should().ContainSingle(change => change.Path == Level2Path && change.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void DeepGraphPatch_WhenDeepPathIsDiscarded_RevertsLeafToSnapshot()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        CreateDeepLeaf(root, out var leaf);
        leaf.LeafField = "base";
        root.Patch(KVPatchOperation.Init($"/{DeepLeafPath}/Animal", "DOG"));
        root.Patch(KVPatchOperation.Set($"/{DeepLeafPath}/Animal/DogName", "Rex"));
        CommitAndContinue(root, model, new List<KVCommit>());

        leaf = GetDeepLeaf(root);
        leaf.LeafField = "draft";
        root.Patch(KVPatchOperation.Init($"/{DeepLeafPath}/Animal", "CAT"));
        root.Patch(KVPatchOperation.Set($"/{DeepLeafPath}/Animal/CatName", "Mittens"));

        root.Patch(KVPatchOperation.Discard($"/{DeepLeafPath}"));

        leaf = GetDeepLeaf(root);
        leaf.LeafField.Should().Be("base");
        leaf.Animal.Should().BeOfType<DeepDogNode>().Subject.DogName.Should().Be("Rex");
        root.GetAllChanges().Changes.Should().BeEmpty();
    }
}
