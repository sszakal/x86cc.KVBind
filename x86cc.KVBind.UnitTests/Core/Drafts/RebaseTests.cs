using AwesomeAssertions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class RebaseTests
{
    public static TheoryData<string, KVValue?, string[]> ItemIdSources => new()
    {
        { "null", null, [] },
        { "tombstone", KVValue.Tombstone, [] },
        { "string[]", KVValue.FromObject(new[] { "a", "b" }), ["a", "b"] },
        { "object[] skips non-strings", KVValue.FromObject(new object[] { "a", 1, "b" }), ["a", "b"] },
        { "JsonElement array skips empty", KVValue.FromObject(System.Text.Json.JsonDocument.Parse("""["a","","b"]""").RootElement.Clone()), ["a", "b"] },
        { "JsonElement non-array", KVValue.FromObject(System.Text.Json.JsonDocument.Parse("""{"a":1}""").RootElement.Clone()), [] },
        { "scalar", KVValue.FromObject("a"), [] },
    };

    // ── Incoming review (non-conflicting upstream changes) ───────────────────

    [Fact]
    public void BeginRebase_NonOverlappingIncomingChange_IsReviewedThenAccepted()
    {
        var v1 = Snapshot(("Title", "A"), ("Desc", "X"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Desc", "Y");
        var v2 = Snapshot(("Title", "B"), ("Desc", "X"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);

        // The upstream Title change is incoming (we never touched Title) and defaults to accept.
        overlay.Conflicts.Should().ContainSingle();
        var incoming = overlay.Conflicts[0];
        incoming.Path.Should().Be("Title");
        incoming.Kind.Should().Be(KVConflictKind.Incoming);
        incoming.RequiresResolution.Should().BeFalse();
        incoming.Resolution.Should().Be(KVConflictResolution.Theirs);

        overlay.FinishRebase(); // accept the default
        Effective(overlay, "Title").Should().Be("B"); // accepted
        Effective(overlay, "Desc").Should().Be("Y");  // our draft preserved
    }

    [Fact]
    public void FinishRebase_RejectIncomingChange_PinsBaseValueAsDraftEdit()
    {
        var v1 = Snapshot(("Title", "A"), ("Desc", "X"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Desc", "Y");
        var v2 = Snapshot(("Title", "B"), ("Desc", "X"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Title", KVConflictResolution.Ours); // reject the incoming change
        overlay.FinishRebase();

        Effective(overlay, "Title").Should().Be("A"); // rejected — base value pinned over the target's
        overlay.Changes.Should().ContainKey("Title"); // surfaces as a counter-edit on the draft
    }

    [Fact]
    public void BeginRebase_WhenOnlyIncomingNoConflicts_StillEntersReview()
    {
        // Even with zero conflicts, a draft with edits reviews the pulled changes before finishing.
        var v1 = Snapshot(("Title", "A"), ("Desc", "X"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Desc", "Y");
        var v2 = Snapshot(("Title", "B"), ("Desc", "X"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.IsRebasing.Should().BeTrue();
        overlay.HasUnresolvedConflicts.Should().BeFalse(); // incoming entries don't block finishing
    }

    [Fact]
    public void BeginRebase_WhenOverlayEmpty_FastForwardsToTarget()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        var v2 = Snapshot(("Title", "B"));

        var outcome = Rebase(overlay, v2);

        outcome.Should().Be(KVRebaseOutcome.CanAutomerge); // empty draft: all upstream changes are incoming
        overlay.FinishRebase();
        Effective(overlay, "Title").Should().Be("B");
    }

    [Fact]
    public void BeginRebase_WhenAlreadyOnTarget_ReportsAlreadyCurrent()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");

        Rebase(overlay, v1).Should().Be(KVRebaseOutcome.AlreadyCurrent);
        overlay.IsRebasing.Should().BeFalse();
    }

    [Fact]
    public void BeginRebase_WhenBothSidesMadeSameChange_ShownAsIncomingResync()
    {
        // Both converged to the same value — not a conflict, but surfaced as a reviewable resync.
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Same");
        var v2 = Snapshot(("Title", "Same"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Incoming);
        overlay.Conflicts[0].RequiresResolution.Should().BeFalse();

        overlay.FinishRebase();
        Effective(overlay, "Title").Should().Be("Same");
    }

    // ── Value conflict ──────────────────────────────────────────────────────

    [Fact]
    public void BeginRebase_WhenBothChangedSamePath_ProducesValueConflict()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);
        overlay.IsRebasing.Should().BeTrue();
        overlay.Conflicts.Should().ContainSingle();

        var conflict = overlay.Conflicts[0];
        conflict.Path.Should().Be("Title");
        conflict.Kind.Should().Be(KVConflictKind.Value);
        (conflict.BaseValue?.Value as string).Should().Be("A");
        (conflict.MainValue?.Value as string).Should().Be("Theirs");
        (conflict.OursValue?.Value as string).Should().Be("Mine");
    }

    [Fact]
    public void FinishRebase_TakeOurs_KeepsDraftValueOnNewBase()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Title", KVConflictResolution.Ours);
        overlay.FinishRebase();

        overlay.IsRebasing.Should().BeFalse();
        Effective(overlay, "Title").Should().Be("Mine");
    }

    [Fact]
    public void FinishRebase_TakeTheirs_DropsDraftAndShowsTarget()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Title", KVConflictResolution.Theirs);
        overlay.FinishRebase();

        Effective(overlay, "Title").Should().Be("Theirs");
        overlay.Changes.Should().NotContainKey("Title");
    }

    [Fact]
    public void FinishRebase_Custom_UsesSuppliedValue()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Title", KVConflictResolution.Custom, "Merged");
        overlay.FinishRebase();

        Effective(overlay, "Title").Should().Be("Merged");
    }

    [Fact]
    public void FinishRebase_WhenConflictsUnresolved_Throws()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2);
        var act = () => overlay.FinishRebase();

        act.Should().Throw<InvalidOperationException>().WithMessage("*conflicts are unresolved*");
    }

    // ── Delete / edit conflict ──────────────────────────────────────────────

    [Fact]
    public void BeginRebase_WhenOverlayDeletedSubtreeTargetEdited_ProducesDeleteEditConflict()
    {
        var v1 = Snapshot(("Items/a/Name", "N"), ("Items/a/Qty", "1"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Remove("Items/a");
        var v2 = Snapshot(("Items/a/Name", "N2"), ("Items/a/Qty", "1"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Items/a");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.DeleteEdit);
    }

    [Fact]
    public void FinishRebase_DeleteEdit_TakeOurs_KeepsDeletion()
    {
        var v1 = Snapshot(("Items/a/Name", "N"), ("Items/a/Qty", "1"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Remove("Items/a");
        var v2 = Snapshot(("Items/a/Name", "N2"), ("Items/a/Qty", "1"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Items/a", KVConflictResolution.Ours);
        overlay.FinishRebase();

        overlay.IsRemoved("Items/a/Name").Should().BeTrue();
    }

    [Fact]
    public void FinishRebase_DeleteEdit_TakeTheirs_RestoresUpstreamSubtree()
    {
        var v1 = Snapshot(("Items/a/Name", "N"), ("Items/a/Qty", "1"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Remove("Items/a");
        var v2 = Snapshot(("Items/a/Name", "N2"), ("Items/a/Qty", "1"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Items/a", KVConflictResolution.Theirs);
        overlay.FinishRebase();

        overlay.IsRemoved("Items/a/Name").Should().BeFalse();
        Effective(overlay, "Items/a/Name").Should().Be("N2");
    }

    [Fact]
    public void Resolve_DeleteEdit_WithCustom_Throws()
    {
        var v1 = Snapshot(("Items/a/Name", "N"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Remove("Items/a");
        var v2 = Snapshot(("Items/a/Name", "N2"));

        Rebase(overlay, v2);
        var act = () => overlay.ResolveConflict("Items/a", KVConflictResolution.Custom, "x");

        act.Should().Throw<InvalidOperationException>().WithMessage("*delete/edit conflict*");
    }

    // ── Cancel / reset ──────────────────────────────────────────────────────

    [Fact]
    public void CancelRebase_KeepsDraftAndStaysOnOriginalBase()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2);
        overlay.CancelRebase();

        overlay.IsRebasing.Should().BeFalse();
        Effective(overlay, "Title").Should().Be("Mine");
    }

    [Fact]
    public void Reset_DropsAllChangesAndResyncsToTarget()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2);
        overlay.Reset(v2);

        overlay.IsRebasing.Should().BeFalse();
        overlay.HasChanges.Should().BeFalse();
        Effective(overlay, "Title").Should().Be("Theirs");
    }

    // ── Structural conflict: $type mismatch on a polymorphic node ────────────

    [Fact]
    public void BeginRebase_WhenBothChangedNodeType_ProducesSingleStructuralConflictAtNode()
    {
        var v1 = Snapshot();
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Claimant/$type", "PERSON");
        overlay.Set("Claimant/FullName", "John");
        var v2 = Snapshot(("Claimant/$type", "COMPANY"), ("Claimant/CompanyName", "Acme"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);

        // One conflict, recorded at the node — not at $type, and no granular field rows.
        overlay.Conflicts.Should().ContainSingle();
        var conflict = overlay.Conflicts[0];
        conflict.Path.Should().Be("Claimant");
        conflict.Kind.Should().Be(KVConflictKind.Structural);
        (conflict.OursValue?.Value as string).Should().Be("PERSON");
        (conflict.MainValue?.Value as string).Should().Be("COMPANY");
    }

    [Fact]
    public void FinishRebase_TypeMismatch_TakeOurs_ReplacesWholeNodeWithoutBleedThrough()
    {
        var v1 = Snapshot();
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Claimant/$type", "PERSON");
        overlay.Set("Claimant/FullName", "John");
        var v2 = Snapshot(("Claimant/$type", "COMPANY"), ("Claimant/CompanyName", "Acme"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Claimant", KVConflictResolution.Ours);
        overlay.FinishRebase();

        Effective(overlay, "Claimant/$type").Should().Be("PERSON");
        Effective(overlay, "Claimant/FullName").Should().Be("John");

        // The target's COMPANY field must not bleed through onto our PERSON node.
        overlay.IsRemoved("Claimant/CompanyName").Should().BeTrue();
        Effective(overlay, "Claimant/CompanyName").Should().BeNull();
    }

    [Fact]
    public void FinishRebase_TypeMismatch_TakeTheirs_ShowsTargetNodeCleanly()
    {
        var v1 = Snapshot();
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Claimant/$type", "PERSON");
        overlay.Set("Claimant/FullName", "John");
        var v2 = Snapshot(("Claimant/$type", "COMPANY"), ("Claimant/CompanyName", "Acme"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Claimant", KVConflictResolution.Theirs);
        overlay.FinishRebase();

        Effective(overlay, "Claimant/$type").Should().Be("COMPANY");
        Effective(overlay, "Claimant/CompanyName").Should().Be("Acme");
        Effective(overlay, "Claimant/FullName").Should().BeNull();
    }

    [Fact]
    public void Resolve_StructuralConflict_WithCustom_Throws()
    {
        var v1 = Snapshot();
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Claimant/$type", "PERSON");
        var v2 = Snapshot(("Claimant/$type", "COMPANY"));

        Rebase(overlay, v2);
        var act = () => overlay.ResolveConflict("Claimant", KVConflictResolution.Custom, "x");

        act.Should().Throw<InvalidOperationException>().WithMessage("*structural conflict*");
    }

    [Fact]
    public void BeginRebase_WhenBothChoseSameType_FallsThroughToLeafMerging()
    {
        // Same $type on both sides — not structural; the differing field is an ordinary value conflict.
        var v1 = Snapshot(("Claimant/$type", "PERSON"), ("Claimant/FullName", "Base"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Claimant/FullName", "Mine");
        var v2 = Snapshot(("Claimant/$type", "PERSON"), ("Claimant/FullName", "Theirs"));

        Rebase(overlay, v2);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Claimant/FullName");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Value);
    }

    // ── Collection membership: non-overlapping additions auto-merge ──────────

    [Fact]
    public void BeginRebase_WhenBothAddedDifferentItems_TargetAddIsIncoming_AcceptMergesBoth()
    {
        // Both sides started from [a]. Ours added x; target added y.
        // Target's y is a non-conflicting incoming add (default accept). Our x is just our draft.
        var v1 = SnapshotV(("Col/$items", Items("a")));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("a", "x"));
        overlay.Set("Col/x/Name", "item x");
        var v2 = SnapshotV(("Col/$items", Items("a", "y")), ("Col/y/Name", "item y"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);

        overlay.Conflicts.Should().ContainSingle();
        var incoming = overlay.Conflicts[0];
        incoming.Path.Should().Be("Col/y");
        incoming.Kind.Should().Be(KVConflictKind.IncomingItem);
        incoming.RequiresResolution.Should().BeFalse();

        overlay.FinishRebase(); // accept the default
        EffectiveItems(overlay, "Col/$items").Should().BeEquivalentTo(["a", "x", "y"]);
        Effective(overlay, "Col/x/Name").Should().Be("item x");
        Effective(overlay, "Col/y/Name").Should().Be("item y");
    }

    [Fact]
    public void FinishRebase_RejectIncomingItemAdd_RemovesItAndRepairsItemsArray()
    {
        var v1 = SnapshotV(("Col/$items", Items("a")));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("a", "x"));
        overlay.Set("Col/x/Name", "item x");
        var v2 = SnapshotV(("Col/$items", Items("a", "y")), ("Col/y/Name", "item y"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Col/y", KVConflictResolution.Ours); // reject the incoming item
        overlay.FinishRebase();

        EffectiveItems(overlay, "Col/$items").Should().BeEquivalentTo(new[] { "a", "x" }); // y dropped
        overlay.IsRemoved("Col/y/Name").Should().BeTrue();
    }

    [Fact]
    public void BeginRebase_WhenOnlyOursChangedNoUpstreamCommits_AlreadyCurrent()
    {
        // Ours added x; upstream committed nothing — there is no gap to rebase, our draft is untouched.
        var v1 = SnapshotV(("Col/$items", Items("a")));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("a", "x"));

        overlay.BeginRebase(v1, []).Should().Be(KVRebaseOutcome.AlreadyCurrent);
        overlay.IsRebasing.Should().BeFalse();
        EffectiveItems(overlay, "Col/$items").Should().BeEquivalentTo(new[] { "a", "x" });
    }

    [Fact]
    public void BeginRebase_WhenBothCleanlyDeleteSameItem_AutoMergesNoConflict()
    {
        // A committed removal drops both the $items entry and the item's leaves, and a real draft
        // deletion tombstones the subtree — so there are no orphan leaves to surface as incoming.
        var v1 = SnapshotV(("Col/$items", Items("a", "b")), ("Col/b/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("a")); // we removed b
        overlay.Remove("Col/b");               // tombstone the item subtree
        var v2 = SnapshotV(("Col/$items", Items("a"))); // target committed the same removal

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.FinishRebase();
        EffectiveItems(overlay, "Col/$items").Should().Equal(new[] { "a" });
    }

    // ── Case A: ours deleted an item, target modified it ─────────────────────

    [Fact]
    public void BeginRebase_CaseA_OursDeletedItemTargetModified_ProducesItemLevelDeleteEditConflict()
    {
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items()); // we deleted x
        overlay.Remove("Col/x");            // tombstone
        var v2 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "updated by target"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);

        overlay.Conflicts.Should().ContainSingle();
        var c = overlay.Conflicts[0];
        c.Path.Should().Be("Col/x");
        c.Kind.Should().Be(KVConflictKind.DeleteEdit);
        c.OursValue.Should().BeNull(); // we deleted it
    }

    [Fact]
    public void FinishRebase_CaseA_TakeOurs_KeepsDeletion()
    {
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items());
        overlay.Remove("Col/x");
        var v2 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "updated"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Col/x", KVConflictResolution.Ours);
        overlay.FinishRebase();

        overlay.IsRemoved("Col/x").Should().BeTrue();
        EffectiveItems(overlay, "Col/$items").Should().BeEmpty();
    }

    [Fact]
    public void FinishRebase_CaseA_TakeTheirs_RestoresItemAndRepairsItemsArray()
    {
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items());
        overlay.Remove("Col/x");
        var v2 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "updated"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Col/x", KVConflictResolution.Theirs);
        overlay.FinishRebase();

        overlay.IsRemoved("Col/x").Should().BeFalse();
        Effective(overlay, "Col/x/Name").Should().Be("updated");
        EffectiveItems(overlay, "Col/$items").Should().Contain("x");
    }

    // ── Case B: target deleted an item, ours modified it ─────────────────────

    [Fact]
    public void BeginRebase_CaseB_TargetDeletedItemOursModified_ProducesItemLevelDeleteEditConflict()
    {
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("x")); // we kept x
        overlay.Set("Col/x/Name", "our edit");
        var v2 = SnapshotV(("Col/$items", Items())); // target deleted x

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);

        overlay.Conflicts.Should().ContainSingle();
        var c = overlay.Conflicts[0];
        c.Path.Should().Be("Col/x");
        c.Kind.Should().Be(KVConflictKind.DeleteEdit);
        c.OursValue.Should().NotBeNull(); // marker: ours has edits
        c.MainValue.Should().BeNull();    // target deleted it
    }

    [Fact]
    public void FinishRebase_CaseB_TakeOurs_KeepsEditedItem()
    {
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("x"));
        overlay.Set("Col/x/Name", "our edit");
        var v2 = SnapshotV(("Col/$items", Items()));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Col/x", KVConflictResolution.Ours);
        overlay.FinishRebase();

        Effective(overlay, "Col/x/Name").Should().Be("our edit");
        EffectiveItems(overlay, "Col/$items").Should().Contain("x");
    }

    [Fact]
    public void FinishRebase_CaseB_TakeTheirs_AcceptsDeletion()
    {
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("x"));
        overlay.Set("Col/x/Name", "our edit");
        var v2 = SnapshotV(("Col/$items", Items()));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Col/x", KVConflictResolution.Theirs);
        overlay.FinishRebase();

        overlay.Changes.Should().NotContainKey("Col/x/Name");
        EffectiveItems(overlay, "Col/$items").Should().BeEmpty();
    }

    // ── Same item, both sides modified → field-level conflicts ───────────────

    [Fact]
    public void BeginRebase_BothEditedSameItemField_ProducesFieldLevelValueConflict()
    {
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "base"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/x/Name", "ours");
        var v2 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "theirs"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);

        // Conflict at the FIELD, not at the item or the $items array.
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Col/x/Name");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Value);
    }

    [Fact]
    public void BeginRebase_BothEditedSameItemFieldToSameValue_ShownAsIncomingResync()
    {
        // Both edited the same field to the same value — not a conflict, surfaced as a resync.
        var v1 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/x/Name", "same");
        var v2 = SnapshotV(("Col/$items", Items("x")), ("Col/x/Name", "same"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Col/x/Name");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Incoming);
        overlay.Conflicts[0].RequiresResolution.Should().BeFalse();

        overlay.FinishRebase();
        Effective(overlay, "Col/x/Name").Should().Be("same");
    }

    // ── Mutual deletions auto-merge ───────────────────────────────────────────

    [Fact]
    public void BeginRebase_BothDeletedSameCollectionItem_AutoMergesNoConflict()
    {
        // Both sides deleted item x. No conflict — the item is simply gone.
        var v1 = SnapshotV(("Col/$items", Items("a", "x")), ("Col/x/Name", "old"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("a"));
        overlay.Remove("Col/x"); // tombstone
        var v2 = SnapshotV(("Col/$items", Items("a"))); // target also deleted x

        var outcome = Rebase(overlay, v2);

        outcome.Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.FinishRebase();
        EffectiveItems(overlay, "Col/$items").Should().Equal(new[] { "a" });
    }

    [Fact]
    public void BeginRebase_BothDroppedSameNestedNode_AutoMergesNoConflict()
    {
        // Both sides DROPped the nested node. No conflict — the slot is simply empty.
        var v1 = Snapshot(("Claimant/$type", "PERSON"), ("Claimant/FullName", "John"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Remove("Claimant"); // we DROPped it — tombstone
        var v2 = Snapshot(); // target also DROPped (snapshot has nothing for Claimant)

        var outcome = Rebase(overlay, v2);

        outcome.Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.FinishRebase();
        overlay.IsRemoved("Claimant").Should().BeTrue();
    }

    [Fact]
    public void BeginRebase_WeDroppedNestedNodeTargetEditedIt_ProducesDeleteEditConflict()
    {
        // We DROPped, target edited a field — this IS a conflict.
        var v1 = Snapshot(("Claimant/$type", "PERSON"), ("Claimant/FullName", "John"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Remove("Claimant");
        var v2 = Snapshot(("Claimant/$type", "PERSON"), ("Claimant/FullName", "Jane"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Claimant");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.DeleteEdit);
        overlay.Conflicts[0].OursValue.Should().BeNull();
    }

    [Fact]
    public void BeginRebase_WeDroppedNestedNodeTargetAlsoDropped_ThenEditedDifferentField_ReviewsIncomingOnly()
    {
        // Both dropped Claimant (mutual — no entry). Target also edited Title (incoming).
        var v1 = Snapshot(("Title", "A"), ("Claimant/$type", "PERSON"), ("Claimant/FullName", "John"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Remove("Claimant");
        var v2 = Snapshot(("Title", "B")); // target dropped Claimant AND edited Title

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Title");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Incoming);

        overlay.FinishRebase(); // accept the incoming Title
        overlay.IsRemoved("Claimant").Should().BeTrue();
        Effective(overlay, "Title").Should().Be("B");
    }

    // ── Commit-driven: fold, zero-change, prefix tombstones ──────────────────

    [Fact]
    public void BeginRebase_TwoCommitsThatCancelOut_CanAutomergeWithEmptyReview()
    {
        // v1 → c1 sets Title=B → c2 reverts Title=A. Net content == v1, but the draft must still advance
        // its base identity to the latest commit. Empty review list, CanAutomerge.
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Desc", "my draft");

        var c1 = Commit(v1, ("Title", "B"));
        var afterC1 = v1.Clone();
        afterC1.Apply(c1);
        var c2 = Commit(afterC1, ("Title", "A")); // undo
        var target = afterC1.Clone();
        target.Apply(c2);

        var outcome = overlay.BeginRebase(target, [c1, c2]);

        outcome.Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.Conflicts.Should().BeEmpty(); // net-zero fold → nothing to review
        overlay.FinishRebase();
        Effective(overlay, "Title").Should().Be("A");
        Effective(overlay, "Desc").Should().Be("my draft");
    }

    [Fact]
    public void BeginRebase_PartialUndo_OnlyTheSurvivingChangeIsIncoming()
    {
        // c1 adds Foo and changes Title; c2 reverts only Title. Foo survives → one incoming entry.
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Desc", "my draft");

        var c1 = Commit(v1, ("Title", "B"), ("Foo", "1"));
        var afterC1 = v1.Clone();
        afterC1.Apply(c1);
        var c2 = Commit(afterC1, ("Title", "A"));
        var target = afterC1.Clone();
        target.Apply(c2);

        overlay.BeginRebase(target, [c1, c2]).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Foo");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Incoming);
    }

    [Fact]
    public void BeginRebase_UpstreamPrefixTombstone_VsOurFieldEdit_DetectsConflict()
    {
        // Upstream drops the whole Claimant node with a single prefix tombstone, while the draft edited a
        // field under it. The fold must expand the prefix against the base to surface the overlap at all —
        // here as a leaf conflict (ours edit vs upstream removal). (Collapsing this to a whole-node
        // delete/edit, as collections do, would be a further enhancement.)
        var v1 = Snapshot(("Claimant/$type", "PERSON"), ("Claimant/FullName", "John"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Claimant/FullName", "My edit");

        var c1 = Commit(v1, ("Claimant", KVValue.Tombstone)); // prefix tombstone, one entry
        var target = v1.Clone();
        target.Apply(c1);

        overlay.BeginRebase(target, [c1]).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);
        overlay.Conflicts.Should().Contain(c =>
            c.RequiresResolution && KVPath.IsSameOrDescendant(c.Path, "Claimant"));
    }

    [Fact]
    public void FromCommits_NormalizesNetZeroFoldToEmpty()
    {
        var v1 = Snapshot(("Title", "A"));
        var c1 = Commit(v1, ("Title", "B"));
        var afterC1 = v1.Clone();
        afterC1.Apply(c1);
        var c2 = Commit(afterC1, ("Title", "A"));

        var theirs = KVOverlay.FromCommits(v1, [c1, c2]);

        theirs.Changes.Should().BeEmpty(); // redundant entry equal to base is dropped
    }

    [Fact]
    public void BeginRebase_WhenAlreadyRebasing_Throws()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        Rebase(overlay, v2);
        var act = () => Rebase(overlay, v2);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already in progress*");
    }

    // ── Incoming $type change on a node the draft left untouched ─────────────

    [Fact]
    public void BeginRebase_WhenTargetChangedTypeOfUntouchedNode_ProducesSingleIncomingAtNode()
    {
        var overlay = DraftOverPersonClaimant();
        var v2 = Snapshot(("Title", "A"), ("Claimant/$type", "COMPANY"), ("Claimant/CompanyName", "Acme"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);

        // One entry at the node; the $type and field changes beneath it are not listed separately.
        var incoming = overlay.Conflicts.Should().ContainSingle().Subject;
        incoming.Path.Should().Be("Claimant");
        incoming.Kind.Should().Be(KVConflictKind.Incoming);
        incoming.RequiresResolution.Should().BeFalse();
        incoming.Resolution.Should().Be(KVConflictResolution.Theirs);
        (incoming.BaseValue?.Value as string).Should().Be("PERSON");
        (incoming.MainValue?.Value as string).Should().Be("COMPANY");
    }

    [Fact]
    public void FinishRebase_AcceptIncomingTypeChange_ShowsTargetNode()
    {
        var overlay = DraftOverPersonClaimant();
        var v2 = Snapshot(("Title", "A"), ("Claimant/$type", "COMPANY"), ("Claimant/CompanyName", "Acme"));

        Rebase(overlay, v2);
        overlay.FinishRebase();

        Effective(overlay, "Claimant/$type").Should().Be("COMPANY");
        Effective(overlay, "Claimant/CompanyName").Should().Be("Acme");
        Effective(overlay, "Claimant/FullName").Should().BeNull();
        Effective(overlay, "Title").Should().Be("Mine");
    }

    [Fact]
    public void FinishRebase_RejectIncomingTypeChange_KeepsBaseNodeWithoutBleedThrough()
    {
        var overlay = DraftOverPersonClaimant();
        var v2 = Snapshot(("Title", "A"), ("Claimant/$type", "COMPANY"), ("Claimant/CompanyName", "Acme"));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Claimant", KVConflictResolution.Ours);
        overlay.FinishRebase();

        Effective(overlay, "Claimant/$type").Should().Be("PERSON");
        Effective(overlay, "Claimant/FullName").Should().Be("John");
        overlay.IsRemoved("Claimant/CompanyName").Should().BeTrue();
        Effective(overlay, "Title").Should().Be("Mine");
    }

    // ── Incoming item removal (target deleted an item the draft left untouched) ──

    [Fact]
    public void BeginRebase_WhenTargetRemovedUntouchedItem_ProducesIncomingItemRemoval()
    {
        var overlay = DraftOverCollection(("Col/b/Name", "item b"));
        var v2 = SnapshotV(("Title", "A"), ("Col/$items", Items("a")));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);

        var incoming = overlay.Conflicts.Should().ContainSingle().Subject;
        incoming.Path.Should().Be("Col/b");
        incoming.Kind.Should().Be(KVConflictKind.IncomingItem);
        incoming.RequiresResolution.Should().BeFalse();
        incoming.BaseValue?.Value.Should().Be(true);
        incoming.MainValue.Should().BeNull();
    }

    [Fact]
    public void FinishRebase_AcceptIncomingItemRemoval_RemovesItem()
    {
        var overlay = DraftOverCollection(("Col/b/Name", "item b"));
        var v2 = SnapshotV(("Title", "A"), ("Col/$items", Items("a")));

        Rebase(overlay, v2);
        overlay.FinishRebase();

        EffectiveItems(overlay, "Col/$items").Should().Equal("a");
        Effective(overlay, "Col/b/Name").Should().BeNull();
        Effective(overlay, "Title").Should().Be("Mine");
    }

    [Fact]
    public void FinishRebase_RejectIncomingItemRemoval_RestoresItemAndRepairsItemsArray()
    {
        var overlay = DraftOverCollection(("Col/b/Name", "item b"));
        var v2 = SnapshotV(("Title", "A"), ("Col/$items", Items("a")));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Col/b", KVConflictResolution.Ours);
        overlay.FinishRebase();

        EffectiveItems(overlay, "Col/$items").Should().Equal("a", "b");
        Effective(overlay, "Col/b/Name").Should().Be("item b");
    }

    [Fact]
    public void FinishRebase_RejectIncomingRemovalOfItemWithNoFields_RestoresItemInItemsArray()
    {
        // The removed item exists only as an id in $items — there is no field data under its path.
        var overlay = DraftOverCollection();
        var v2 = SnapshotV(("Title", "A"), ("Col/$items", Items("a")));

        Rebase(overlay, v2);
        overlay.ResolveConflict("Col/b", KVConflictResolution.Ours);
        overlay.FinishRebase();

        EffectiveItems(overlay, "Col/$items").Should().Equal("a", "b");
    }

    [Fact]
    public void FinishRebase_WhenNeitherSideTouchedCollection_KeepsItsMembershipOutOfTheDraft()
    {
        var overlay = DraftOverCollection(("Col/b/Name", "item b"));
        var v2 = SnapshotV(("Title", "A"), ("Desc", "upstream"), ("Col/$items", Items("a", "b")), ("Col/b/Name", "item b"));

        Rebase(overlay, v2);
        overlay.Changes.Should().NotContainKey("Col/$items");
        overlay.FinishRebase();

        EffectiveItems(overlay, "Col/$items").Should().Equal("a", "b");
        overlay.Changes.Keys.Should().Equal("Title");
    }

    [Fact]
    public void FinishRebase_WhenOnlyTargetAddedItem_KeepsExistingItems()
    {
        var overlay = DraftOverCollection(("Col/b/Name", "item b"));
        var v2 = SnapshotV(("Title", "A"), ("Col/$items", Items("a", "b", "c")), ("Col/b/Name", "item b"), ("Col/c/Name", "item c"));

        Rebase(overlay, v2);
        overlay.Conflicts.Should().ContainSingle().Which.Path.Should().Be("Col/c");
        overlay.FinishRebase();

        EffectiveItems(overlay, "Col/$items").Should().Equal("a", "b", "c");
    }

    // ── $items order ──

    [Fact]
    public void FinishRebase_WhenTargetReorderedAndDraftAdded_KeepsTargetOrderThenDraftAdditionsInDraftOrder()
    {
        var v1 = SnapshotV(("Col/$items", Items("a", "b", "c")));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("a", "b", "c", "y", "x"));
        var v2 = SnapshotV(("Col/$items", Items("c", "b", "a")));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.FinishRebase();

        EffectiveItems(overlay, "Col/$items").Should().Equal("c", "b", "a", "y", "x");
    }

    [Fact]
    public void BeginRebase_WhenTargetDeletedItemsDraftEdited_AppendsThemInBaseOrderAfterDraftAdditions()
    {
        var v1 = SnapshotV(("Col/$items", Items("c", "b", "a")), ("Col/a/Name", "a"), ("Col/c/Name", "c"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/c/Name", "c mine");
        overlay.Set("Col/a/Name", "a mine");
        overlay.Set("Col/$items", Items("c", "b", "a", "z"));
        var v2 = SnapshotV(("Col/$items", Items("b")));

        Rebase(overlay, v2);

        overlay.Conflicts.Select(c => c.Path).Should().Equal("Col/a", "Col/c");
        EffectiveItems(overlay, "Col/$items").Should().Equal("b", "z", "c", "a");
    }

    [Fact]
    public void Merge_ItemsArrayOrder_IsTheSameForRepeatedRuns()
    {
        var ids = Enumerable.Range(0, 200).Select(i => $"id{i:D3}").ToArray();
        var baseSnapshot = SnapshotV(("Col/$items", Items(ids[..100])));
        var theirs = new Dictionary<string, KVValue>(StringComparer.Ordinal) { ["Col/$items"] = Items([.. ids[..100].Reverse()]) };
        var oursIds = ids[..100].Where((_, i) => i % 3 != 0).Concat(ids[100..].Reverse()).ToArray();
        var ours = new Dictionary<string, KVValue>(StringComparer.Ordinal) { ["Col/$items"] = Items(oursIds) };

        var merged = KVMerge.ExtractItemIds(KVMerge.Merge(baseSnapshot, theirs, ours).MergedItemArrays["Col/$items"]);

        var expected = ids[..100].Reverse().Where(id => oursIds.Contains(id)).Concat(ids[100..].Reverse());
        merged.Should().Equal(expected);
    }

    [Fact]
    public void ExtractItemIds_KeepsArrayOrderAndDropsLaterDuplicates()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""["b","a","b"]""");

        KVMerge.ExtractItemIds(Items("b", "a", "b")).Should().Equal("b", "a");
        KVMerge.ExtractItemIds(KVValue.FromObject(document.RootElement.Clone())).Should().Equal("b", "a");
    }

    // ── $items arrays as they come back from JSON (object[] / JsonElement) ──

    [Fact]
    public void BeginRebase_WhenItemsArraysAreUntypedJson_MergesMembership()
    {
        using var baseItems = System.Text.Json.JsonDocument.Parse("""["a"]""");
        var v1 = SnapshotV(("Col/$items", KVValue.FromObject(baseItems.RootElement.Clone())));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Col/$items", Items("a", "x"));
        var v2 = SnapshotV(("Col/$items", KVValue.FromObject(new object[] { "a", "y" })), ("Col/y/Name", "item y"));

        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.CanAutomerge);
        overlay.Conflicts.Should().ContainSingle().Which.Path.Should().Be("Col/y");

        overlay.FinishRebase();
        EffectiveItems(overlay, "Col/$items").Should().Equal("a", "y", "x");
    }

    [Theory]
    [MemberData(nameof(ItemIdSources))]
    public void ExtractItemIds_ReadsEachStoredShape(string shape, KVValue? value, string[] expected)
    {
        KVMerge.ExtractItemIds(value).Should().Equal(expected, because: shape);
    }

    // ── Persisted in-progress rebase ─────────────────────────────────────────

    [Fact]
    public void RestoreRebaseState_AfterJsonRoundTrip_ResumesAndFinishesLikeTheOriginal()
    {
        var v1 = Snapshot(("Title", "A"), ("Desc", "X"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"), ("Desc", "Y"));
        Rebase(overlay, v2).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);
        overlay.ResolveConflict("Desc", KVConflictResolution.Ours); // reject the incoming Desc change

        // Persist the same parts a draft document stores, then rebuild a fresh overlay from them.
        var json = System.Text.Json.JsonSerializer.Serialize(new PersistedRebase(overlay.Snapshot, overlay.Changes, overlay.RebaseTarget!, overlay.Conflicts));
        var persisted = System.Text.Json.JsonSerializer.Deserialize<PersistedRebase>(json)!;
        var restored = KVOverlay.Create(persisted.BaseSnapshot, "editor");
        restored.Changes = persisted.Changes;
        restored.RestoreRebaseState(persisted.RebaseTarget, persisted.Conflicts);

        restored.IsRebasing.Should().BeTrue();
        restored.HasUnresolvedConflicts.Should().BeTrue();
        restored.Conflicts.Select(c => (c.Path, c.Kind, c.Resolution)).Should().Equal(
            ("Desc", KVConflictKind.Incoming, KVConflictResolution.Ours),
            ("Title", KVConflictKind.Value, KVConflictResolution.Unresolved));

        restored.ResolveConflict("Title", KVConflictResolution.Custom, "Merged");
        restored.FinishRebase();

        restored.IsRebasing.Should().BeFalse();
        Effective(restored, "Title").Should().Be("Merged");
        Effective(restored, "Desc").Should().Be("X");
        restored.Snapshot.Data.Should().ContainKey("Desc").WhoseValue.Value.Should().Be("Y"); // base swapped to target
    }

    // ── Rebase state guards ──────────────────────────────────────────────────

    [Fact]
    public void RebaseCommands_WhenNoRebaseInProgress_Throw()
    {
        var overlay = KVOverlay.Create(Snapshot(("Title", "A")), "editor");

        ((Action)(() => overlay.ResolveConflict("Title", KVConflictResolution.Ours)))
            .Should().Throw<InvalidOperationException>().WithMessage("No rebase is in progress.");
        ((Action)overlay.FinishRebase)
            .Should().Throw<InvalidOperationException>().WithMessage("No rebase is in progress.");
        ((Action)overlay.CancelRebase)
            .Should().Throw<InvalidOperationException>().WithMessage("No rebase is in progress.");
    }

    [Fact]
    public void ResolveConflict_WhenPathHasNoConflict_Throws()
    {
        var overlay = KVOverlay.Create(Snapshot(("Title", "A")), "editor");
        overlay.Set("Title", "Mine");
        Rebase(overlay, Snapshot(("Title", "Theirs")));

        var act = () => overlay.ResolveConflict("Desc", KVConflictResolution.Ours);

        act.Should().Throw<InvalidOperationException>().WithMessage("No conflict at path 'Desc'.");
    }

    [Fact]
    public void Resolve_IncomingChange_WithCustom_Throws()
    {
        var overlay = KVOverlay.Create(Snapshot(("Title", "A"), ("Desc", "X")), "editor");
        overlay.Set("Desc", "Y");
        Rebase(overlay, Snapshot(("Title", "B"), ("Desc", "X")));

        var act = () => overlay.ResolveConflict("Title", KVConflictResolution.Custom, "C");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not valid for an incoming change*");
    }

    [Fact]
    public void Resolve_BackToUnresolved_Throws()
    {
        var overlay = KVOverlay.Create(Snapshot(("Title", "A")), "editor");
        overlay.Set("Title", "Mine");
        Rebase(overlay, Snapshot(("Title", "Theirs")));

        var act = () => overlay.ResolveConflict("Title", KVConflictResolution.Unresolved);

        act.Should().Throw<ArgumentException>().WithMessage("Cannot resolve an entry back to Unresolved.*");
    }

    // Base: PERSON claimant with a field, plus Title. Draft only edits Title, so Claimant is untouched.
    private static KVOverlay DraftOverPersonClaimant()
    {
        var v1 = Snapshot(("Title", "A"), ("Claimant/$type", "PERSON"), ("Claimant/FullName", "John"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        return overlay;
    }

    // Base: collection [a, b] (plus optional item fields) and Title. Draft only edits Title.
    private static KVOverlay DraftOverCollection(params (string Path, string Value)[] itemFields)
    {
        var v1 = SnapshotV(("Title", "A"), ("Col/$items", Items("a", "b")));
        foreach (var (path, value) in itemFields)
            v1.Data[path] = value;
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        return overlay;
    }

    private static KVSnapshot Snapshot(params (string Path, string Value)[] data)
    {
        var snapshot = new KVSnapshot();
        foreach (var (path, value) in data)
            snapshot.Data[path] = value;
        return snapshot;
    }

    // For tests that mix string and KVValue entries (e.g. $items as string[]).
    private static KVSnapshot SnapshotV(params (string Path, KVValue Value)[] data)
    {
        var snapshot = new KVSnapshot();
        foreach (var (path, value) in data)
            snapshot.Data[path] = value;
        return snapshot;
    }

    private static KVValue Items(params string[] ids) => KVValue.FromObject(ids);

    private static string? Effective(KVOverlay overlay, string path) =>
        overlay.TryGet(path, out var value) ? value?.Value as string : null;

    private static string[] EffectiveItems(KVOverlay overlay, string path)
    {
        if (!overlay.TryGet(path, out var value) || value is null) return [];
        return [.. KVMerge.ExtractItemIds(value)];
    }

    // Test bridge: rebase against a target snapshot by deriving the single upstream commit that takes the
    // overlay's base to that target. Lets existing snapshot-based setups drive the commit-based BeginRebase.
    private static KVRebaseOutcome Rebase(KVOverlay overlay, KVSnapshot target)
    {
        var commit = DiffToCommit(overlay.Snapshot, target);
        return overlay.BeginRebase(target, commit is null ? [] : [commit]);
    }

    private static KVCommit? DiffToCommit(KVSnapshot from, KVSnapshot to)
    {
        var commit = new KVCommit
        {
            CommitId = Guid.NewGuid(),
            PreviousCommitId = from.LastCommitId,
            Timestamp = DateTimeOffset.UtcNow,
        };
        foreach (var key in from.Data.Keys.Concat(to.Data.Keys).Distinct(StringComparer.Ordinal))
        {
            from.Data.TryGetValue(key, out var fv);
            to.Data.TryGetValue(key, out var tv);
            if (KVMerge.ValueEquals(fv, tv)) continue;
            commit.Changes[key] = tv ?? KVValue.Tombstone;
        }

        return commit.Changes.Count == 0 ? null : commit;
    }

    // Builds a real commit with the given changes (string value, Items(...) array, or KVValue.Tombstone),
    // used for tests that need genuine prefix tombstones rather than the per-leaf diff bridge.
    private static KVCommit Commit(KVSnapshot baseSnapshot, params (string Path, KVValue Value)[] changes)
    {
        var commit = new KVCommit
        {
            CommitId = Guid.NewGuid(),
            PreviousCommitId = baseSnapshot.LastCommitId,
            Timestamp = DateTimeOffset.UtcNow,
        };
        foreach (var (path, value) in changes)
            commit.Changes[path] = value;
        return commit;
    }

    private sealed record PersistedRebase(KVSnapshot BaseSnapshot, KVDictionary Changes, KVSnapshot RebaseTarget, List<KVConflict> Conflicts);
}
