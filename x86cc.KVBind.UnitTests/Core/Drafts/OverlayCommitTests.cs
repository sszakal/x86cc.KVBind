using AwesomeAssertions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class OverlayCommitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OverlayCommit_WhenUserIsEmpty_Throws(string? user)
    {
        var snapshot = new KVSnapshot();
        var act = () => KVOverlay.Create(snapshot, user!);

        act.Should().Throw<ArgumentException>().WithMessage("Overlay user cannot be empty.*");
    }

    [Fact]
    public void OverlayCommit_WhenUserIsClearedAfterCreation_Throws()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "editor");
        var act = () => overlay.User = "";

        act.Should().Throw<ArgumentException>().WithMessage("Overlay user cannot be empty.*");
    }

    [Fact]
    public void OverlayCommit_WhenCreated_CapturesBaseCommitAndUser()
    {
        var snapshot = new KVSnapshot
        {
            LastCommitId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };

        var overlay = KVOverlay.Create(snapshot, "editor");

        overlay.BaseCommitId.Should().Be(snapshot.LastCommitId);
        overlay.User.Should().Be("editor");
    }

    [Fact]
    public void SnapshotClone_WhenSourceIsMutated_PreservesCopiedState()
    {
        var snapshot = new KVSnapshot
        {
            LastCommitId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            ModifiedBy = "editor"
        };
        snapshot.Data["Title"] = "Original";

        var clone = snapshot.Clone();
        snapshot.Data["Title"] = "Changed";
        snapshot.LastCommitId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        clone.LastCommitId.Should().Be(Guid.Parse("33333333-3333-3333-3333-333333333333"));
        clone.ModifiedBy.Should().Be("editor");
        clone.Data.Should().ContainKey("Title").WhoseValue.Should().Be("Original");
    }

    [Fact]
    public void OverlayCommit_WhenConvertedToCommit_CopiesUserTimestampAndChanges()
    {
        var timestamp = new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero);
        var snapshot = new KVSnapshot();
        var overlay = KVOverlay.Create(snapshot, "editor");
        overlay.Set("Title", "Draft");
        overlay.Remove("Items/old");

        var commit = overlay.ToCommit(timestamp);

        commit.PreviousCommitId.Should().Be(snapshot.LastCommitId);
        commit.User.Should().Be("editor");
        commit.Timestamp.Should().Be(timestamp);
        commit.Changes.Should().ContainKey("Title").WhoseValue.Should().Be("Draft");
        commit.Changes.Should().ContainKey("Items/old").WhoseValue.Should().Be(KVValue.Tombstone);
    }

    [Fact]
    public void OverlayCommit_WhenConvertedToCommit_CopiesCollectionsWithoutSharingMutableState()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "editor");
        overlay.Set("Title", "Draft");
        overlay.Remove("Items/old");

        var commit = overlay.ToCommit(DateTimeOffset.UtcNow);

        overlay.Set("Title", "Changed after commit");
        overlay.Set("Other", "New value");
        overlay.RestorePath("Items/old");

        commit.Changes.Should().ContainKey("Title").WhoseValue.Should().Be("Draft");
        commit.Changes.Should().NotContainKey("Other");
        commit.Changes.Should().ContainKey("Items/old").WhoseValue.Should().Be(KVValue.Tombstone);
    }

    [Fact]
    public void OverlayCommit_WhenOverlayIsEmpty_CreatesEmptyCommitWithMetadata()
    {
        var timestamp = new DateTimeOffset(2026, 6, 1, 12, 30, 0, TimeSpan.Zero);
        var snapshot = new KVSnapshot();
        var overlay = KVOverlay.Create(snapshot, "editor");

        var commit = overlay.ToCommit(timestamp);

        commit.User.Should().Be("editor");
        commit.Timestamp.Should().Be(timestamp);
        commit.Changes.Should().BeEmpty();
    }
}
