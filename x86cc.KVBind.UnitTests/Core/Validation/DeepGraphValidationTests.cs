using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class DeepGraphValidationTests : DeepGraphTestBase
{
    [Fact]
    public void DeepGraphValidation_WhenLeafNestedNodeRequiredFieldIsMissing_ReturnsCanonicalPath()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        var leafPath = CreateDeepLeaf(root, out var leaf);
        leaf.LeafField = "leaf";
        root.Patch(KVPatchOperation.Init($"/{leafPath}/Animal", "DOG"));

        var validation = root.Validate();

        validation.Errors.Should().ContainSingle(error =>
            error.Path == $"{leafPath}/Animal/DogName"
            && error.Code == "required");
    }

    [Fact]
    public void DeepGraphValidation_WhenLeafNestedNodeRequiredFieldIsSet_ReturnsNoNestedNodeErrors()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        var leafPath = CreateDeepLeaf(root, out var leaf);
        leaf.LeafField = "leaf";
        root.Patch(KVPatchOperation.Init($"/{leafPath}/Animal", "DOG"));
        root.Patch(KVPatchOperation.Set($"/{leafPath}/Animal/DogName", "Rex"));

        var validation = root.Validate();

        validation.Errors.Should().NotContain(error => error.Path.StartsWith($"{leafPath}/Animal", StringComparison.Ordinal));
    }

    [Fact]
    public void DeepGraphValidation_WhenCommitHasDeepNestedValidationError_Throws()
    {
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        var leafPath = CreateDeepLeaf(root, out _);
        root.Patch(KVPatchOperation.Init($"/{leafPath}/Animal", "DOG"));

        var act = () => root.CreateCommit(DateTimeOffset.UtcNow);

        act.Should().Throw<KVChangeSetValidationException>()
            .Where(exception => exception.Errors.Any(error => error.Path == $"{leafPath}/Animal/DogName" && error.Code == "required"));
    }
}
