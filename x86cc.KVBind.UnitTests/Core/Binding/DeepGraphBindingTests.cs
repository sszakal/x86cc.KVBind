using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class DeepGraphBindingTests : DeepGraphTestBase
{
    [Fact]
    public void DeepGraphBinding_WhenLeafNestedNodeIsEdited_StoresExpectedCanonicalPaths()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        var leafPath = CreateDeepLeaf(root, out var leaf);

        leaf.LeafField = "leaf-1";
        root.Patch(KVPatchOperation.Init($"/{leafPath}/Animal", "DOG"));
        root.Patch(KVPatchOperation.Set($"/{leafPath}/Animal/DogName", "Rex"));

        model.Overlay.AddedOrChanged.Should().ContainKey($"{leafPath}/LeafField").WhoseValue.Should().Be("leaf-1");
        model.Overlay.AddedOrChanged.Should().ContainKey($"{leafPath}/Animal/$type").WhoseValue.Should().Be("DOG");
        model.Overlay.AddedOrChanged.Should().ContainKey($"{leafPath}/Animal/DogName").WhoseValue.Should().Be("Rex");

        var dog = leaf.Animal.Should().BeOfType<DeepDogNode>().Subject;
        dog.DogName.Should().Be("Rex");
    }

    [Fact]
    public void DeepGraphBinding_WhenCommittedAndRebound_HydratesLeafNestedNode()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        var leafPath = CreateDeepLeaf(root, out var leaf);

        leaf.LeafField = "leaf-1";
        root.Patch(KVPatchOperation.Init($"/{leafPath}/Animal", "DOG"));
        root.Patch(KVPatchOperation.Set($"/{leafPath}/Animal/DogName", "Rex"));
        CommitAndContinue(root, model, new List<KVCommit>());

        var hydratedLeaf = GetDeepLeaf(root);
        hydratedLeaf.LeafField.Should().Be("leaf-1");
        var dog = hydratedLeaf.Animal.Should().BeOfType<DeepDogNode>().Subject;
        dog.DogName.Should().Be("Rex");
    }
}
