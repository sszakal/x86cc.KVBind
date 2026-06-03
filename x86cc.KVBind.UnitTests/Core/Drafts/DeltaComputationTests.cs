using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class DeltaComputationTests
{
    [Fact]
    public void DeltaComputation_WhenChildIsRemoved_EmitsSingleSyntheticRemovedDelta()
    {
        var snapshot = new KVSnapshot();
        var model = new KVModelRoot(KVOverlay.Create(snapshot, "test"));
        model.Set("Name", "base");

        var child = model.EnsureChildModel("Items/123");
        child.Set("Amount", 10m);

        snapshot.Apply(model.Overlay.ToCommit(DateTimeOffset.UtcNow));
        model.ReplaceOverlay(KVOverlay.Create(snapshot, model.Overlay.User));
        model.MarkChildRemoved("Items/123");

        var deltas = model.ComputeDeltas().Flatten();

        deltas.Should().ContainSingle(delta => delta.Path == "Items/123" && delta.ChangeType == KVChangeDeltaType.Removed);
        deltas.Should().NotContain(delta => delta.Path == "Items/123/Amount");
        
        model.UnmarkChildRemoved("Items/123");

        deltas = model.ComputeDeltas().Flatten();
        
        deltas.Should().BeEmpty();
    }

    [Fact]
    public void DeltaComputation_WhenFieldIsRemoved_EmitsRemovedDelta()
    {
        var snapshot = new KVSnapshot();
        var model = new KVModelRoot(KVOverlay.Create(snapshot, "test"));
        model.Set("Status", "active");

        snapshot.Apply(model.Overlay.ToCommit(DateTimeOffset.UtcNow));
        model.ReplaceOverlay(KVOverlay.Create(snapshot, model.Overlay.User));
        model.Remove("Status");

        var deltas = model.ComputeDeltas().Flatten();

        deltas.Should().ContainSingle(delta => delta.Path == "Status" && delta.ChangeType == KVChangeDeltaType.Removed);
    }

    [Fact]
    public void DeltaComputation_WhenCollectionItemIsAdded_EmitsItemAddedWithoutMetadataPaths()
    {
        var model = new KVModelRoot();
        var items = model.EnsureCollectionModel("Items");

        var item = items.EnsureItemModel("2");
        item.Set("$id", "2");
        item.Set("$type", "Line");
        item.Set("Name", "added");

        var deltas = model.ComputeDeltas().Flatten();

        deltas.Should().ContainSingle(delta => delta.Path == "Items/2" && delta.ChangeType == KVChangeDeltaType.Added);
        deltas.Should().NotContain(delta => delta.Path.Contains("$", StringComparison.Ordinal));
    }

    [Fact]
    public void DeltaComputation_WhenCollectionItemMetadataChanges_DoesNotExposeMetadataPaths()
    {
        var snapshot = new KVSnapshot();
        var model = new KVModelRoot(KVOverlay.Create(snapshot, "test"));
        var item = model.EnsureCollectionModel("Items").EnsureItemModel("1");
        item.Set("$id", "1");
        item.Set("$type", "Line");
        item.Set("Name", "base");

        snapshot.Apply(model.Overlay.ToCommit(DateTimeOffset.UtcNow));
        model.ReplaceOverlay(KVOverlay.Create(snapshot, model.Overlay.User));
        item.Set("$id", "updated");
        item.Set("$type", "Other");
        item.Set("Name", "updated");

        var deltas = model.ComputeDeltas().Flatten();

        deltas.Should().ContainSingle(delta => delta.Path == "Items/1/Name" && delta.ChangeType == KVChangeDeltaType.Updated);
        deltas.Should().NotContain(delta => delta.Path.Contains("$", StringComparison.Ordinal));
        deltas.Should().NotContain(delta => delta.Path == "Items/1");
    }

    [Fact]
    public void DeltaComputation_WhenNestedNodeTypeChanges_EmitsSlotDelta()
    {
        var model = new KVModelRoot();
        var animal = model.EnsureChildModel("Animal");

        animal.Set("$type", "DOG");
        animal.Set("Name", "Fido");

        var deltas = model.ComputeDeltas().Flatten();

        deltas.Should().ContainSingle(delta => delta.Path == "Animal" && delta.ChangeType == KVChangeDeltaType.Added);
    }
}
