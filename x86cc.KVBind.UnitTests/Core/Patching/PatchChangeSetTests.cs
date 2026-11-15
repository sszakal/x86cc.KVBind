using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.UnitTests.Core.Binding;

namespace x86cc.KVBind.UnitTests.Core;

// KVPatchResult.Changes is the change set of one patch: its operations and the reactions they triggered,
// isolated from earlier patches and sessions. KVRootNode.GetAllChanges() is everything the draft holds.
public class PatchChangeSetTests : KVModelTestBase
{
    public PatchChangeSetTests()
    {
        this.RegisterModelDefinition<JpvRoot>(builder =>
        {
            builder.Field(x => x.Amount);
            builder.Field(x => x.Name);
        });
    }

    [Fact]
    public void EachPatch_ReportsOnlyItsOwnChanges_ReadAtAnyTime()
    {
        var root = this.CreateRoot<JpvRoot>();

        var r1 = root.Patch(KVPatchOperation.Set("/Name", "a"));
        var r2 = root.Patch(KVPatchOperation.Set("/Amount", 1m));

        r1.Changes.Select(c => c.Path).Should().Equal("Name");
        r2.Changes.Select(c => c.Path).Should().Equal("Amount");
        root.GetAllChanges().Changes.Select(c => c.Path).Should().BeEquivalentTo("Name", "Amount");
    }

    [Fact]
    public void APatch_OnAReloadedDraft_LeavesOutTheChangesOfEarlierSessions()
    {
        // Yesterday's session changed Name; the draft was saved and is reloaded today.
        var model = new KVModelRoot(KVOverlay.Create(new KVSnapshot(), TestUser));
        var earlier = this.CreateRoot<JpvRoot>(model);
        earlier.Patch(KVPatchOperation.Set("/Name", "draft"));
        var reloaded = KVOverlay.Create(model.Overlay.Snapshot.Clone(), TestUser);
        reloaded.Changes = new KVDictionary(model.Overlay.Changes);
        var root = this.CreateRoot<JpvRoot>(new KVModelRoot(reloaded));

        var result = root.Patch(KVPatchOperation.Set("/Amount", 5m));

        result.Changes.Select(c => c.Path).Should().Equal("Amount");
        root.GetAllChanges().Changes.Select(c => c.Path).Should().BeEquivalentTo("Name", "Amount");
    }

    [Fact]
    public void AFieldChange_CarriesItsOldAndNewValue()
    {
        var root = this.CreateRoot<JpvRoot>();
        root.Patch(KVPatchOperation.Set("/Name", "a"));

        var change = root.Patch(KVPatchOperation.Set("/Name", "b")).Changes.Should().ContainSingle().Subject;

        change.ChangeType.Should().Be(KVChangeDeltaType.Updated);
        change.OldValue.Should().Be("a");
        change.NewValue.Should().Be("b");
    }

    [Fact]
    public void SeveralWritesToOneField_AreOneChange_FromTheFirstOldToTheLastNew()
    {
        var root = this.CreateRoot<JpvRoot>();
        root.Patch(KVPatchOperation.Set("/Name", "a"));

        var result = root.Patch(KVPatchOperation.Set("/Name", "b"), KVPatchOperation.Set("/Name", "c"));
        var change = result.Changes.Should().ContainSingle().Subject;
        change.OldValue.Should().Be("a");
        change.NewValue.Should().Be("c");

        root.Patch(KVPatchOperation.Set("/Name", "d"), KVPatchOperation.Set("/Name", "c")).Changes
            .Should().BeEmpty("written back to the value it had before the patch");
    }

    [Fact]
    public void TheResult_IsFixedWhenThePatchReturns()
    {
        var model = new KVModelRoot(KVOverlay.Create(new KVSnapshot(), TestUser));
        var root = this.CreateRoot<JpvRoot>(model);

        var result = root.Patch(KVPatchOperation.Set("/Name", "a"));
        root.Patch(KVPatchOperation.Set("/Amount", 1m));
        CommitSetup(model);

        result.Changes.Select(c => c.Path).Should().Equal("Name");
    }

    [Fact]
    public void ReactionWrites_ArePartOfThePatchThatTriggeredThem()
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();
        root.Patch(RecursiveCollectionTreeTests.Branch(levels: 3, prefix: "p"));

        var result = root.Patch(KVPatchOperation.Set("/Parts/p/Detail/Parts/p-1/Detail/Parts/p-1-1/Cost", 5m));

        result.Changes.Select(c => c.Path).Should().Equal(
            "Parts/p/Detail/Parts/p-1/Detail/Parts/p-1-1/Cost",
            "Parts/p/Detail/Parts/p-1/Detail/Subtotal",
            "Parts/p/Detail/Subtotal",
            "Total");
    }

    [Fact]
    public void AnItemAddedAndFilledInOnePatch_IsOneAddedChange()
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();

        var result = root.Patch(
            KVPatchOperation.Add("/Parts", new KVAddPatchPayload("q")),
            KVPatchOperation.Set("/Parts/q/Name", "q"),
            KVPatchOperation.Set("/Parts/q/Cost", 2m));

        // The item's fields are part of its Added entry. Total is new too: it was never stored before.
        result.Changes.Select(c => (c.Path, c.ChangeType)).Should().BeEquivalentTo([("Parts/q", KVChangeDeltaType.Added), ("Total", KVChangeDeltaType.Added)]);
    }

    [Fact]
    public void RemovingAnItem_IsOneRemovedChange()
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();
        root.Patch(RecursiveCollectionTreeTests.Branch(levels: 2, prefix: "p"));

        var result = root.Patch(KVPatchOperation.Remove("/Parts/p"));

        result.Changes.Should().Contain(c => c.Path == "Parts/p" && c.ChangeType == KVChangeDeltaType.Removed);
        result.Changes.Should().NotContain(c => c.Path.StartsWith("Parts/p/", StringComparison.Ordinal));
    }

    // An item added and removed again in one patch never existed for the caller: nothing is reported.
    [Fact]
    public void AnItemAddedAndRemovedInOnePatch_LeavesNoChange()
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();
        root.Patch(RecursiveCollectionTreeTests.Branch(levels: 1, prefix: "p"));

        var result = root.Patch(
            KVPatchOperation.Add("/Parts", new KVAddPatchPayload("x")),
            KVPatchOperation.Set("/Parts/x/Name", "a"),
            KVPatchOperation.Remove("/Parts/x"));

        result.Changes.Should().NotContain(c => c.Path.StartsWith("Parts/x", StringComparison.Ordinal));
    }

    // A discarded (or reset) subtree is one entry; earlier writes of the same patch beneath it are gone.
    [Fact]
    public void ADiscard_StandsForEverythingBeneathIt()
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();
        root.Patch(RecursiveCollectionTreeTests.Branch(levels: 1, prefix: "p"));
        var snapshot = root.Model.Overlay.Snapshot;
        snapshot.Apply(root.CreateCommit(DateTimeOffset.UtcNow));
        root = KVRootNode.Create<Binding.TreeRoot>(KVOverlay.Create(snapshot, "alice"), RecursiveCollectionTreeTests.Definition);

        var result = root.Patch(
            KVPatchOperation.Set("/Parts/p/Name", "changed"),
            KVPatchOperation.Discard("/Parts/p"));

        result.Changes.Should().NotContain(c => c.Path == "Parts/p/Name");
        result.Changes.Should().ContainSingle(c => c.Path == "Parts/p").Which.ChangeType.Should().Be(KVChangeDeltaType.Updated);
        root.Parts.GetById("p")!.Name.Should().Be("p");
    }

    // DISCARD drops the draft's values under a path; an item the draft added stays a member, emptied.
    [Fact]
    public void DiscardingADraftAddedItem_IsAnUpdateOfTheItem()
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();
        root.Patch(RecursiveCollectionTreeTests.Branch(levels: 1, prefix: "p"));

        var result = root.Patch(KVPatchOperation.Discard("/Parts/p"));

        root.Parts.GetById("p")!.Name.Should().BeNull();
        result.Changes.Should().ContainSingle(c => c.Path.StartsWith("Parts/p", StringComparison.Ordinal))
            .Which.ChangeType.Should().Be(KVChangeDeltaType.Updated);
    }

    // Sequences are compared by content, not by reference.
    [Fact]
    public void AListSetToTheSameContent_IsNoChange()
    {
        this.RegisterModelDefinition<PcsRoot>(builder => builder.Field(x => x.Tags));
        var root = this.CreateRoot<PcsRoot>();
        root.Patch(KVPatchOperation.Set("/Tags", new[] { "a", "b" }));

        var result = root.Patch(KVPatchOperation.Set("/Tags", new[] { "a", "b" }));

        result.Changes.Should().BeEmpty();
    }

    // Many first-time writes in one patch: each is reported (covering is looked up per path, not by scanning).
    [Fact]
    public void AThousandFirstTimeWrites_AreAllReported()
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();
        var operations = new List<KVPatchOperation>();
        for (var i = 0; i < 300; i++)
        {
            operations.Add(KVPatchOperation.Add("/Parts", new KVAddPatchPayload($"n{i}")));
        }

        root.Patch(operations);
        var sets = Enumerable.Range(0, 300).Select(i => KVPatchOperation.Set($"/Parts/n{i}/Name", $"name {i}"));

        var result = root.Patch(sets);

        result.Changes.Count(c => c.ChangeType == KVChangeDeltaType.Added && c.Path.EndsWith("/Name", StringComparison.Ordinal)).Should().Be(300);
    }
}

public partial class PcsRoot : KVRootNode
{
    [KVBind("Tags")]
    public partial string[]? Tags { get; set; }
}
