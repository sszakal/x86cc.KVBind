using AwesomeAssertions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class RebaseTests
{
    private static readonly Guid Aggregate = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static KVSnapshot Snapshot(params (string Path, string Value)[] data)
    {
        var snapshot = new KVSnapshot { AggregateId = Aggregate, Version = Guid.NewGuid() };
        foreach (var (path, value) in data)
            snapshot.Data[path] = value;
        return snapshot;
    }

    private static string? Effective(KVOverlay overlay, string path) =>
        overlay.TryGet(path, out var value) ? value?.Value as string : null;

    // ── Clean fast-forward ──────────────────────────────────────────────────

    [Fact]
    public void BeginRebase_WhenChangesDoNotOverlap_FastForwardsAndKeepsDraft()
    {
        var v1 = Snapshot(("Title", "A"), ("Desc", "X"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Desc", "Y");

        var v2 = Snapshot(("Title", "B"), ("Desc", "X"));

        var outcome = overlay.BeginRebase(v2);

        outcome.Should().Be(KVRebaseOutcome.Merged);
        overlay.IsRebasing.Should().BeFalse();
        overlay.BaseSnapshotVersion.Should().Be(v2.Version);
        Effective(overlay, "Title").Should().Be("B"); // target shows through
        Effective(overlay, "Desc").Should().Be("Y");  // draft preserved
    }

    [Fact]
    public void BeginRebase_WhenOverlayEmpty_FastForwardsToTarget()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        var v2 = Snapshot(("Title", "B"));

        var outcome = overlay.BeginRebase(v2);

        outcome.Should().Be(KVRebaseOutcome.Merged);
        overlay.BaseSnapshotVersion.Should().Be(v2.Version);
        Effective(overlay, "Title").Should().Be("B");
    }

    [Fact]
    public void BeginRebase_WhenAlreadyOnTarget_ReportsAlreadyCurrent()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");

        overlay.BeginRebase(v1).Should().Be(KVRebaseOutcome.AlreadyCurrent);
        overlay.IsRebasing.Should().BeFalse();
    }

    [Fact]
    public void BeginRebase_WhenBothSidesMadeSameChange_NoConflict()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Same");
        var v2 = Snapshot(("Title", "Same"));

        overlay.BeginRebase(v2).Should().Be(KVRebaseOutcome.Merged);
        overlay.IsRebasing.Should().BeFalse();
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

        overlay.BeginRebase(v2).Should().Be(KVRebaseOutcome.ConflictsPending);
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

        overlay.BeginRebase(v2);
        overlay.ResolveConflict("Title", KVConflictResolution.Ours);
        overlay.FinishRebase();

        overlay.IsRebasing.Should().BeFalse();
        overlay.BaseSnapshotVersion.Should().Be(v2.Version);
        Effective(overlay, "Title").Should().Be("Mine");
    }

    [Fact]
    public void FinishRebase_TakeTheirs_DropsDraftAndShowsTarget()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2).Should().Be(KVRebaseOutcome.ConflictsPending);
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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
        overlay.CancelRebase();

        overlay.IsRebasing.Should().BeFalse();
        overlay.BaseSnapshotVersion.Should().Be(v1.Version);
        Effective(overlay, "Title").Should().Be("Mine");
    }

    [Fact]
    public void Reset_DropsAllChangesAndResyncsToTarget()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        overlay.BeginRebase(v2);
        overlay.Reset(v2);

        overlay.IsRebasing.Should().BeFalse();
        overlay.HasChanges.Should().BeFalse();
        overlay.BaseSnapshotVersion.Should().Be(v2.Version);
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

        overlay.BeginRebase(v2).Should().Be(KVRebaseOutcome.ConflictsPending);

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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
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

        overlay.BeginRebase(v2);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Claimant/FullName");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Value);
    }

    // ── Structural conflict: $items collection membership ────────────────────

    [Fact]
    public void BeginRebase_WhenBothChangedItemsArray_ProducesStructuralConflictAtItems()
    {
        var v1 = Snapshot(("Damaged/$items", "[a]"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Damaged/$items", "[a,b]");
        var v2 = Snapshot(("Damaged/$items", "[a,c]"));

        overlay.BeginRebase(v2).Should().Be(KVRebaseOutcome.ConflictsPending);
        overlay.Conflicts.Should().ContainSingle();
        overlay.Conflicts[0].Path.Should().Be("Damaged/$items");
        overlay.Conflicts[0].Kind.Should().Be(KVConflictKind.Structural);
    }

    [Fact]
    public void FinishRebase_ItemsConflict_TakeTheirs_DropsOursMembership()
    {
        var v1 = Snapshot(("Damaged/$items", "[a]"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Damaged/$items", "[a,b]");
        var v2 = Snapshot(("Damaged/$items", "[a,c]"));

        overlay.BeginRebase(v2);
        overlay.ResolveConflict("Damaged/$items", KVConflictResolution.Theirs);
        overlay.FinishRebase();

        Effective(overlay, "Damaged/$items").Should().Be("[a,c]");
        overlay.Changes.Should().NotContainKey("Damaged/$items");
    }

    [Fact]
    public void BeginRebase_WhenAlreadyRebasing_Throws()
    {
        var v1 = Snapshot(("Title", "A"));
        var overlay = KVOverlay.Create(v1.Clone(), "editor");
        overlay.Set("Title", "Mine");
        var v2 = Snapshot(("Title", "Theirs"));

        overlay.BeginRebase(v2);
        var act = () => overlay.BeginRebase(v2);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already in progress*");
    }
}
