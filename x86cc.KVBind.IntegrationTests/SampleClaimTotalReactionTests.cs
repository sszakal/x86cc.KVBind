using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.Sample.Api.Claims;

namespace x86cc.KVBind.IntegrationTests;

public sealed class SampleClaimTotalReactionTests
{
    [Fact]
    public void DamagedItemChanges_RecalculateClaimedTotal()
    {
        var definition = new InsuranceClaimDefinitionFactory().Definition;
        var snapshot = new KVSnapshot();
        var overlay = KVOverlay.Create(snapshot, "adjuster-a");
        var claim = Bind(overlay, definition);
        var firstItemId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondItemId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        claim.Patch(
            KVPatchOperation.Add("/DamagedItems", new KVAddPatchPayload(firstItemId)),
            KVPatchOperation.Set($"/DamagedItems/{firstItemId:D}/EstimatedAmount", 100m));

        claim.ClaimedTotal.Should().Be(100m);

        claim.Patch(
            KVPatchOperation.Add("/DamagedItems", new KVAddPatchPayload(secondItemId)),
            KVPatchOperation.Set($"/DamagedItems/{secondItemId:D}/EstimatedAmount", 25.50m));

        claim.ClaimedTotal.Should().Be(125.50m);

        claim.Patch(KVPatchOperation.Remove($"/DamagedItems/{firstItemId:D}"));

        claim.ClaimedTotal.Should().Be(25.50m);

        var commit = claim.CreateCommit(DateTimeOffset.UtcNow);
        snapshot.Apply(commit);
        var reloaded = Bind(KVOverlay.Create(snapshot.Clone(), "reader"), definition);

        reloaded.ClaimedTotal.Should().Be(25.50m);
        reloaded.DamagedItems.Should().ContainSingle().Which.EstimatedAmount.Should().Be(25.50m);
    }

    private static InsuranceClaim Bind(KVOverlay overlay, KVNodeDefinition definition)
    {
        return KVRootNode.Create<InsuranceClaim>(overlay, definition);
    }
}
