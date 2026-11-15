using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.UnitTests.Core.Binding;

namespace x86cc.KVBind.UnitTests.Core;

// DISCARD of a collection item undoes everything the draft did to that item, its membership included: an
// item the draft added disappears, an item it removed comes back at its committed position, and an item it
// only edited keeps its place with its committed values.
public class DiscardItemTests
{
    [Fact]
    public void AnItemTheDraftAdded_Disappears()
    {
        var root = Committed("a", "b");
        root.Patch(KVPatchOperation.Add("/Parts", new KVAddPatchPayload("new")), KVPatchOperation.Set("/Parts/new/Name", "new"));

        root.Patch(KVPatchOperation.Discard("/Parts/new"));

        root.Parts.GetActiveItemIds().Should().Equal("a", "b");
        root.GetAllChanges().Changes.Should().BeEmpty("nothing is left to change");
        root.Total.Should().Be(2m, "the reactions recount without it");
    }

    [Fact]
    public void AnItemTheDraftRemoved_ComesBack_AtItsCommittedPosition()
    {
        var root = Committed("a", "b", "c", "d");
        root.Patch(KVPatchOperation.Remove("/Parts/b"), KVPatchOperation.Add("/Parts", new KVAddPatchPayload("x")));
        root.Patch(KVPatchOperation.Set("/Parts/x/Name", "x"));

        root.Patch(KVPatchOperation.Discard("/Parts/b"));

        root.Parts.GetActiveItemIds().Should().Equal("a", "b", "c", "d", "x");
        root.Parts.GetById("b")?.Name.Should().Be("b");
        root.Total.Should().Be(5m);
    }

    [Fact]
    public void ARemovedFirstItem_ComesBackFirst_EvenWhenItsNeighboursMoved()
    {
        var root = Committed("a", "b", "c");
        root.Patch(KVPatchOperation.Remove("/Parts/a"), KVPatchOperation.Move("/Parts/c", 0));

        root.Patch(KVPatchOperation.Discard("/Parts/a"));

        root.Parts.GetActiveItemIds().Should().Equal(["a", "c", "b"], "the draft's move of c stays; a is back before its committed successors");
    }

    [Fact]
    public void AnItemTheDraftOnlyEdited_KeepsItsPlace_AndGetsItsCommittedValues()
    {
        var root = Committed("a", "b", "c");
        root.Patch(KVPatchOperation.Set("/Parts/b/Name", "changed"), KVPatchOperation.Move("/Parts/b", 2));

        root.Patch(KVPatchOperation.Discard("/Parts/b"));

        root.Parts.GetById("b")?.Name.Should().Be("b");
        root.Parts.GetActiveItemIds().Should().Equal(["a", "c", "b"], "discarding an item's edits does not undo a move of the list");
    }

    [Fact]
    public void DiscardingTheLastChangeOfTheList_LeavesNoMembershipChange()
    {
        var root = Committed("a", "b");
        root.Patch(KVPatchOperation.Remove("/Parts/a"));

        root.Patch(KVPatchOperation.Discard("/Parts/a"));

        root.Model.Overlay.Changes.Should().NotContainKey("Parts/$items");
        root.GetAllChanges().Changes.Should().BeEmpty();
    }

    [Fact]
    public void TheRestoredItem_IsReportedAsAdded_InThePatchChangeSet()
    {
        var root = Committed("a", "b");
        root.Patch(KVPatchOperation.Remove("/Parts/b"));

        var result = root.Patch(KVPatchOperation.Discard("/Parts/b"));

        result.Changes.Should().Contain(c => c.Path == "Parts/b" && c.ChangeType == KVChangeDeltaType.Added);
    }

    // Committed parts with the given ids (each named after its id, cost 1), as a fresh draft.
    private static Binding.TreeRoot Committed(params string[] ids)
    {
        var root = RecursiveCollectionTreeTests.CreateRoot();
        root.Patch(ids.SelectMany(id => new[]
        {
            KVPatchOperation.Add("/Parts", new KVAddPatchPayload(id)),
            KVPatchOperation.Set($"/Parts/{id}/Name", id),
        }));
        var snapshot = root.Model.Overlay.Snapshot.Clone();
        snapshot.Apply(root.CreateCommit(DateTimeOffset.UtcNow));
        return KVRootNode.Create<Binding.TreeRoot>(KVOverlay.Create(snapshot, "alice"), RecursiveCollectionTreeTests.Definition);
    }
}
