using AwesomeAssertions;
using System.Text.Json;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class KVOverlayTests
{
    [Fact]
    public void Remove_SetsTombstoneAtPath()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");

        overlay.Remove("Orders/uuid1");

        overlay.HasRemovedPath("Orders/uuid1").Should().BeTrue();
        overlay.Changes.Should().ContainKey("Orders/uuid1").WhoseValue.Should().Be(KVValue.Tombstone);
    }

    [Fact]
    public void IsRemoved_ReturnsTrueForDescendantsOfTombstonedPath()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("Orders/uuid1");

        overlay.IsRemoved("Orders/uuid1").Should().BeTrue();
        overlay.IsRemoved("Orders/uuid1/Amount").Should().BeTrue();
        overlay.IsRemoved("Orders/uuid1/Lines/uuid2/Name").Should().BeTrue();
    }

    [Fact]
    public void IsRemoved_ReturnsFalseForUnrelatedPaths()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("Orders/uuid1");

        overlay.IsRemoved("Orders").Should().BeFalse();
        overlay.IsRemoved("Orders/uuid2").Should().BeFalse();
        overlay.IsRemoved("Title").Should().BeFalse();
    }

    [Fact]
    public void Remove_ClearsDescendantsFromChanges()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Set("Orders/uuid1/Amount", new KVValue<decimal>(10m));
        overlay.Set("Orders/uuid1/Name", new KVValue<string>("test"));

        overlay.Remove("Orders/uuid1");

        overlay.Changes.Should().ContainKey("Orders/uuid1").WhoseValue.Should().Be(KVValue.Tombstone);
        overlay.Changes.Should().NotContainKey("Orders/uuid1/Amount");
        overlay.Changes.Should().NotContainKey("Orders/uuid1/Name");
    }

    [Fact]
    public void Set_OverTombstonedPath_ReplacesTombstoneWithValue()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("key");

        overlay.Set("key", new KVValue<string>("restored"));

        overlay.HasRemovedPath("key").Should().BeFalse();
        overlay.TryGet("key", out var val).Should().BeTrue();
        val!.Value.Should().Be("restored");
    }

    [Fact]
    public void AncestorTombstone_BlocksDescendantReads()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("parent");

        // Setting under a tombstoned path stores the value in Changes,
        // but reading it back is blocked by the ancestor tombstone.
        overlay.Set("parent/child", new KVValue<string>("value"));

        overlay.TryGet("parent/child", out _).Should().BeFalse();
        overlay.IsRemoved("parent/child").Should().BeTrue();
    }

    [Fact]
    public void RestorePath_RemovesDirectTombstone()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("key");

        overlay.RestorePath("key");

        overlay.HasRemovedPath("key").Should().BeFalse();
        overlay.IsRemoved("key").Should().BeFalse();
    }

    [Fact]
    public void RestorePath_OnChildDoesNotRemoveAncestorTombstone()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("parent");

        overlay.RestorePath("parent/child");

        overlay.IsRemoved("parent/child").Should().BeTrue();
    }

    [Fact]
    public void HasDraftState_ReturnsTrueWhenTombstonePresent()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("key");

        overlay.HasDraftState("key").Should().BeTrue();
        overlay.HasDraftState("other").Should().BeFalse();
    }

    [Fact]
    public void Discard_RemovesTombstoneEntry()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Remove("Orders/uuid1");

        overlay.Discard("Orders/uuid1");

        overlay.IsRemoved("Orders/uuid1").Should().BeFalse();
        overlay.Changes.Should().NotContainKey("Orders/uuid1");
    }

    [Fact]
    public void Clear_RemovesAllChangesIncludingTombstones()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Set("key1", new KVValue<string>("value"));
        overlay.Remove("key2");

        overlay.Clear();

        overlay.Changes.Should().BeEmpty();
        overlay.IsRemoved("key2").Should().BeFalse();
    }

    [Fact]
    public void Tombstone_RoundTripsThroughJsonSerialization()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "test");
        overlay.Set("Title", new KVValue<string>("hello"));
        overlay.Remove("Orders/uuid1");
        var commit = overlay.ToCommit(DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(commit);
        var deserialized = JsonSerializer.Deserialize<KVCommit>(json)!;

        deserialized.Changes.Should().ContainKey("Title").WhoseValue.Should().NotBe(KVValue.Tombstone);
        deserialized.Changes.Should().ContainKey("Orders/uuid1").WhoseValue.Should().Be(KVValue.Tombstone);
    }

    [Fact]
    public void Apply_TombstoneCommit_RemovesPrefixFromSnapshot()
    {
        var snapshot = new KVSnapshot();
        var setup = KVOverlay.Create(snapshot, "test");
        setup.Set("Orders/uuid1/Amount", new KVValue<decimal>(10m));
        setup.Set("Orders/uuid1/Name", new KVValue<string>("item"));
        snapshot.Apply(setup.ToCommit(DateTimeOffset.UtcNow));
        snapshot.Data.Should().ContainKey("Orders/uuid1/Amount");

        var overlay = KVOverlay.Create(snapshot.Clone(), "test");
        overlay.Remove("Orders/uuid1");
        snapshot.Apply(overlay.ToCommit(DateTimeOffset.UtcNow));

        snapshot.Data.Should().NotContainKey("Orders/uuid1/Amount");
        snapshot.Data.Should().NotContainKey("Orders/uuid1/Name");
    }
}
