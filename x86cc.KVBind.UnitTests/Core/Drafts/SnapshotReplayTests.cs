using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class SnapshotReplayTests : DeepGraphTestBase
{
    [Fact]
    public void SnapshotReplay_WhenCommitListIsEmpty_DoesNotChangeSnapshot()
    {
        var snapshot = new KVSnapshot
        {
            AggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LastCommitId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            LastCommitTimestamp = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            ModifiedBy = "editor"
        };
        snapshot.Data["Title"] = "Existing";
        var version = snapshot.Version;
        var timestamp = snapshot.Timestamp;

        snapshot.Apply(Array.Empty<KVCommit>());

        snapshot.Data.Should().ContainKey("Title").WhoseValue.Should().Be("Existing");
        snapshot.LastCommitId.Should().Be(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        snapshot.LastCommitTimestamp.Should().Be(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        snapshot.ModifiedBy.Should().Be("editor");
        snapshot.Version.Should().Be(version);
        snapshot.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void SnapshotReplay_WhenCommitAggregateIdDoesNotMatch_Throws()
    {
        var snapshot = new KVSnapshot { AggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var commit = new KVCommit
        {
            AggregateId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CommitId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            User = "editor",
            Timestamp = DateTimeOffset.UtcNow
        };

        var act = () => snapshot.Apply([commit]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*aggregate id*");
    }

    [Fact]
    public void SnapshotReplay_WhenCommitsAreAppliedToExistingBaseSnapshot_AppliesOnlyLaterChanges()
    {
        var baseSnapshot = new KVSnapshot
        {
            AggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LastCommitId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        baseSnapshot.Data["Title"] = "Base";
        baseSnapshot.Data["Items/old/Name"] = "Remove me";
        var first = new KVCommit
        {
            AggregateId = baseSnapshot.AggregateId,
            CommitId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            PreviousCommitId = baseSnapshot.LastCommitId,
            User = "editor",
            Timestamp = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            AddedOrChanged = new Dictionary<string, object?> { ["Title"] = "Changed" }
        };
        var second = new KVCommit
        {
            AggregateId = baseSnapshot.AggregateId,
            CommitId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            PreviousCommitId = first.CommitId,
            User = "editor",
            Timestamp = new DateTimeOffset(2026, 6, 1, 12, 1, 0, TimeSpan.Zero),
            Removed = new HashSet<string> { "Items/old" },
            AddedOrChanged = new Dictionary<string, object?> { ["Items/new/Name"] = "New" }
        };

        baseSnapshot.Apply([first, second]);

        baseSnapshot.Data.Should().ContainKey("Title").WhoseValue.Should().Be("Changed");
        baseSnapshot.Data.Should().NotContainKey("Items/old/Name");
        baseSnapshot.Data.Should().ContainKey("Items/new/Name").WhoseValue.Should().Be("New");
        baseSnapshot.LastCommitId.Should().Be(second.CommitId);
        baseSnapshot.LastCommitTimestamp.Should().Be(second.Timestamp);
        baseSnapshot.ModifiedBy.Should().Be("editor");
    }

    [Fact]
    public void SnapshotReplay_WhenLaterCommitInBatchFails_KeepsEarlierAppliedCommits()
    {
        var snapshot = new KVSnapshot { AggregateId = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var first = new KVCommit
        {
            AggregateId = snapshot.AggregateId,
            CommitId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            User = "editor",
            Timestamp = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
            AddedOrChanged = new Dictionary<string, object?> { ["Title"] = "Changed" }
        };
        var brokenSecond = new KVCommit
        {
            AggregateId = snapshot.AggregateId,
            CommitId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            PreviousCommitId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            User = "editor",
            Timestamp = new DateTimeOffset(2026, 6, 1, 12, 1, 0, TimeSpan.Zero),
            AddedOrChanged = new Dictionary<string, object?> { ["Other"] = "Should not apply" }
        };

        var act = () => snapshot.Apply([first, brokenSecond]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*commit chain*");
        snapshot.Data.Should().ContainKey("Title").WhoseValue.Should().Be("Changed");
        snapshot.Data.Should().NotContainKey("Other");
        snapshot.LastCommitId.Should().Be(first.CommitId);
    }

    [Fact]
    public void SnapshotReplay_WhenDeepGraphCommitsAreReplayed_MatchesCurrentSnapshot()
    {
        var commits = new List<KVCommit>();
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        var leafPath = CreateDeepLeaf(root, out var leaf);

        leaf.LeafField = "leaf-1";
        root.Patch(KVPatchOperation.Init($"/{leafPath}/Animal", "DOG"));
        root.Patch(KVPatchOperation.Set($"/{leafPath}/Animal/DogName", "Rex"));
        CommitAndContinue(root, model, commits);

        leaf = GetDeepLeaf(root);
        leaf.LeafField = "leaf-2";
        CommitAndContinue(root, model, commits);

        root.Patch(KVPatchOperation.Init($"/{leafPath}/Animal", "CAT"));
        root.Patch(KVPatchOperation.Set($"/{leafPath}/Animal/CatName", "Mittens"));
        CommitAndContinue(root, model, commits);

        var sibling = GetLevel3(root).Level4Collection.Create(Guid.Parse("55555555-5555-5555-5555-555555555555"));
        sibling.LeafField = "sibling";
        CommitAndContinue(root, model, commits);

        var replayed = new KVSnapshot { AggregateId = model.Snapshot.AggregateId };
        replayed.Apply(commits);

        replayed.Data.Should().BeEquivalentTo(model.Snapshot.Data);
        replayed.LastCommitId.Should().Be(model.Snapshot.LastCommitId);
        replayed.LastCommitTimestamp.Should().Be(model.Snapshot.LastCommitTimestamp);
        replayed.ModifiedBy.Should().Be(model.Snapshot.ModifiedBy);
        replayed.Data.Should().NotContainKey($"{leafPath}/Animal/DogName");
        replayed.Data.Should().ContainKey($"{leafPath}/Animal/CatName").WhoseValue.Should().Be("Mittens");
    }

    [Fact]
    public void SnapshotReplay_WhenCommitChainIsBroken_Throws()
    {
        var commits = new List<KVCommit>();
        var model = new KVModelRoot();
        var root = CreateRoot<DeepNestedCollectionRoot>(model);
        CreateDeepLeaf(root, out var leaf);

        leaf.LeafField = "leaf-1";
        CommitAndContinue(root, model, commits);

        leaf = GetDeepLeaf(root);
        leaf.LeafField = "leaf-2";
        CommitAndContinue(root, model, commits);

        var replayed = new KVSnapshot { AggregateId = model.Snapshot.AggregateId };
        var act = () => replayed.Apply(commits.Skip(1));

        act.Should().Throw<InvalidOperationException>().WithMessage("*commit chain*");
    }
}
