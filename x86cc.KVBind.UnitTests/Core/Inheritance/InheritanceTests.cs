using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Inheritance;

// A "contract" aggregate where a child inherits root members from its parent (master) read-only:
//   Reference   — normal editable field
//   MasterTerms — inherited field (required)
//   Header      — inherited field group (Region, Currency)
//   Parties     — inherited collection (Name)
//   Signatory   — inherited nested node (Person | Company)
public class InheritanceTests
{
    private static KVNodeDefinition BuildDefinition()
    {
        var builder = new KVBindBuilder<Contract>();
        builder.Field(x => x.Reference);
        builder.Field(x => x.Status);
        builder.Field(x => x.MasterTerms, f => f
            .Inherited()
            .Default("DEFAULT-TERMS")
            .Validation(p => p.For<ContractProfile>(r => r.Required())));
        builder.FieldGroup(x => x.Header, group =>
        {
            group.Field(x => x.Region);
            group.Field(x => x.Currency);
        }, options => options.Inherited());
        builder.Collection(x => x.Parties, collection =>
        {
            collection.Inherited();
            collection.Item<ContractParty>(item => item.Field(x => x.Name));
        });
        builder.NestedNode(x => x.Signatory, nested =>
        {
            nested.Inherited();
            nested.Bind<PersonSignatory>("PERSON", p => { p.Field(x => x.Name); p.Field(x => x.Title); });
            nested.Bind<CompanySignatory>("COMPANY", c => { c.Field(x => x.Name); c.Field(x => x.RegistrationNo); });
        });
        return builder.Build();
    }

    // The parent (master) committed snapshot, with values at the inherited paths.
    private static KVSnapshot ParentSnapshot()
    {
        var snapshot = new KVSnapshot { LastCommitId = Guid.NewGuid() };
        snapshot.Data["MasterTerms"] = "Net 30";
        snapshot.Data["Header/Region"] = "EU";
        snapshot.Data["Header/Currency"] = "EUR";
        snapshot.Data["Parties/$items"] = KVValue.FromObject(new[] { "pa" });
        snapshot.Data["Parties/pa/$type"] = "ContractParty";
        snapshot.Data["Parties/pa/Name"] = "Acme";
        snapshot.Data["Signatory/$type"] = "PERSON";
        snapshot.Data["Signatory/Name"] = "Jane";
        snapshot.Data["Signatory/Title"] = "CEO";
        return snapshot;
    }

    private static Contract BindChild(KVNodeDefinition definition, KVSnapshot parent, KVSnapshot? childSnapshot = null)
    {
        var overlay = KVOverlay.Create((childSnapshot ?? new KVSnapshot()).Clone(), "child");
        overlay.MarkAsChild(); // a child binding requires the mark under the authoritative rule
        return KVRootNode.Create<Contract>(overlay, definition, parent);
    }

    [Fact]
    public void Child_reads_inherited_values_from_the_parent()
    {
        var child = BindChild(BuildDefinition(), ParentSnapshot());

        child.MasterTerms.Should().Be("Net 30");
        child.Header.Region.Should().Be("EU");
        child.Header.Currency.Should().Be("EUR");
        child.Parties.Single().Name.Should().Be("Acme");
        var signatory = child.Signatory.Should().BeOfType<PersonSignatory>().Subject;
        signatory.Name.Should().Be("Jane");
        signatory.Title.Should().Be("CEO");
    }

    [Fact]
    public void Child_can_edit_non_inherited_fields()
    {
        var child = BindChild(BuildDefinition(), ParentSnapshot());

        child.Reference = "CHILD-1";

        child.Reference.Should().Be("CHILD-1");
    }

    [Fact]
    public void Child_write_to_inherited_field_throws()
    {
        var child = BindChild(BuildDefinition(), ParentSnapshot());

        var act = () => child.MasterTerms = "tampered";

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Child_write_into_inherited_subtrees_throws()
    {
        var child = BindChild(BuildDefinition(), ParentSnapshot());

        ((Action)(() => child.Header.Region = "US")).Should().Throw<InvalidOperationException>();
        ((Action)(() => child.Parties.Create(Guid.NewGuid()))).Should().Throw<InvalidOperationException>();
        ((Action)(() => child.Signatory!.Name = "Other")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Patch_targeting_an_inherited_path_throws()
    {
        var child = BindChild(BuildDefinition(), ParentSnapshot());

        var act = () => child.Patch(KVPatchOperation.Set("/MasterTerms", "tampered"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Inherited_values_never_appear_in_draft_changes_or_commits()
    {
        var child = BindChild(BuildDefinition(), ParentSnapshot());

        child.Reference = "CHILD-1";

        var changes = child.GetAllChanges().Changes;
        changes.Should().ContainSingle().Which.Path.Should().Be("Reference");

        var commit = child.CreateCommit(DateTimeOffset.UtcNow);
        commit.Changes.Keys.Should().Contain("Reference");
        commit.Changes.Keys.Should().NotContain(key =>
            key == "MasterTerms" || key.StartsWith("Header") || key.StartsWith("Parties") || key.StartsWith("Signatory"));
    }

    [Fact]
    public void Master_without_a_parent_can_edit_the_same_members()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "master");
        var master = KVRootNode.Create<Contract>(overlay, BuildDefinition()); // no parent → editable

        master.MasterTerms = "Net 60";
        master.Header.Region = "APAC";
        var party = master.Parties.Create(Guid.NewGuid());
        party.Name = "Globex";

        master.MasterTerms.Should().Be("Net 60");
        master.Header.Region.Should().Be("APAC");
        master.Parties.Single().Name.Should().Be("Globex");
    }

    [Fact]
    public void Child_validation_sees_inherited_values_and_passes_when_the_parent_is_valid()
    {
        var definition = BuildDefinition();

        // Parent supplies the required (inherited) MasterTerms → child validates clean.
        BindChild(definition, ParentSnapshot()).Validate().Errors.Should().BeEmpty();

        // Parent missing it → the inherited required field surfaces as an error (effective read participates).
        var bareParent = new KVSnapshot();
        var errors = BindChild(definition, bareParent).Validate().Errors;
        errors.Should().Contain(e => e.Path == "MasterTerms" && e.Code == "required");
    }

    [Fact]
    public void CreateNew_child_does_not_default_into_inherited_members()
    {
        // A child created fresh with a parent must not try to seed/default inherited members (would throw).
        var overlay = KVOverlay.Create(new KVSnapshot(), "child");
        overlay.MarkAsChild();
        var act = () => KVRootNode.CreateNew<Contract>(overlay, BuildDefinition(), ParentSnapshot());

        act.Should().NotThrow();
    }

    [Fact]
    public void Builder_rejects_inherited_on_a_non_root_member()
    {
        var builder = new KVBindBuilder<Contract>();

        var act = () => builder.FieldGroup(x => x.Header,
            group => group.Field(x => x.Region, f => f.Inherited()),
            options => { });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Inherited_field_default_applies_on_master_but_is_skipped_on_a_child()
    {
        var definition = BuildDefinition();

        // Master (no parent) created fresh → the inherited field's default applies; it is editable.
        var masterOverlay = KVOverlay.Create(new KVSnapshot(), "master");
        var master = KVRootNode.CreateNew<Contract>(masterOverlay, definition);
        master.MasterTerms.Should().Be("DEFAULT-TERMS");

        // Child (with parent) created fresh → the default is skipped; the value is inherited from the parent.
        var childOverlay = KVOverlay.Create(new KVSnapshot(), "child");
        childOverlay.MarkAsChild();
        var child = KVRootNode.CreateNew<Contract>(childOverlay, definition, ParentSnapshot());
        child.MasterTerms.Should().Be("Net 30");
    }

    [Fact]
    public void Detach_copies_parent_data_into_the_child_and_makes_it_editable()
    {
        var child = BindChild(BuildDefinition(), ParentSnapshot());

        // Before detach: inherited, read-only.
        ((Action)(() => child.MasterTerms = "x")).Should().Throw<InvalidOperationException>();

        child.Detach();

        // The full inherited subtree was copied in and is now the child's own.
        child.MasterTerms.Should().Be("Net 30");
        child.Header.Region.Should().Be("EU");
        child.Parties.Single().Name.Should().Be("Acme");
        child.Signatory.Should().BeOfType<PersonSignatory>().Which.Name.Should().Be("Jane");

        // Edits now succeed and diverge from the (former) parent.
        child.MasterTerms = "Net 90";
        child.MasterTerms.Should().Be("Net 90");

        // The copied values (and the edit) are real draft changes / committable.
        var commit = child.CreateCommit(DateTimeOffset.UtcNow);
        commit.Changes.Keys.Should().Contain(new[] { "MasterTerms", "Header/Region", "Parties/$items", "Signatory/$type" });
        commit.Changes["MasterTerms"].Value.Should().Be("Net 90");
    }

    [Fact]
    public void Detach_persists_after_commit_and_rebind_without_a_parent()
    {
        var definition = BuildDefinition();
        var childSnapshot = new KVSnapshot();

        var overlay = KVOverlay.Create(childSnapshot.Clone(), "child");
        overlay.MarkAsChild();
        var child = KVRootNode.Create<Contract>(overlay, definition, ParentSnapshot());
        child.Detach();
        child.MasterTerms = "Net 90";
        childSnapshot.Apply(child.CreateCommit(DateTimeOffset.UtcNow));

        // Rebind WITHOUT a parent: the formerly-inherited values persist and are editable.
        var reboundOverlay = KVOverlay.Create(childSnapshot.Clone(), "child");
        var rebound = KVRootNode.Create<Contract>(reboundOverlay, definition);
        rebound.MasterTerms.Should().Be("Net 90");
        rebound.Header.Region.Should().Be("EU");

        rebound.MasterTerms = "Net 120";
        rebound.MasterTerms.Should().Be("Net 120");
    }

    [Fact]
    public void Detach_without_a_parent_is_a_noop()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "master");
        var master = KVRootNode.Create<Contract>(overlay, BuildDefinition());

        master.Detach();

        master.GetAllChanges().Changes.Should().BeEmpty();
    }

    [Fact]
    public void A_marked_child_bound_without_a_parent_throws()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "child");
        overlay.MarkAsChild();

        var act = () => KVRootNode.Create<Contract>(overlay, BuildDefinition());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void An_unmarked_master_bound_with_a_parent_throws()
    {
        var overlay = KVOverlay.Create(new KVSnapshot(), "master");

        var act = () => KVRootNode.Create<Contract>(overlay, BuildDefinition(), ParentSnapshot());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CreateChild_produces_a_marked_child_that_reads_the_parent()
    {
        var definition = BuildDefinition();
        // A committed master, holding the inherited data.
        var parent = KVRootNode.Create<Contract>(KVOverlay.Create(ParentSnapshot(), "master"), definition);

        var child = parent.CreateChild<Contract>();

        // The child reads the parent's inherited values…
        child.MasterTerms.Should().Be("Net 30");
        child.Header.Region.Should().Be("EU");
        // …its own non-inherited fields are editable…
        child.Reference = "CHILD-1";
        child.Reference.Should().Be("CHILD-1");
        // …and its first commit carries the child mark.
        var commit = child.CreateCommit(DateTimeOffset.UtcNow);
        commit.Changes.Keys.Should().Contain("$parent");
    }

    [Fact]
    public void Detach_removes_the_mark_so_a_later_bind_needs_no_parent()
    {
        var definition = BuildDefinition();
        var childSnapshot = new KVSnapshot();
        var overlay = KVOverlay.Create(childSnapshot.Clone(), "child");
        overlay.MarkAsChild();
        var child = KVRootNode.Create<Contract>(overlay, definition, ParentSnapshot());

        child.Detach();
        childSnapshot.Apply(child.CreateCommit(DateTimeOffset.UtcNow));

        // The mark is gone → binding without a parent now succeeds (and with a parent now throws).
        var rebind = () => KVRootNode.Create<Contract>(KVOverlay.Create(childSnapshot.Clone(), "child"), definition);
        rebind.Should().NotThrow();
        var rebindWithParent = () => KVRootNode.Create<Contract>(KVOverlay.Create(childSnapshot.Clone(), "child"), definition, ParentSnapshot());
        rebindWithParent.Should().Throw<InvalidOperationException>();
    }

    // ── Rebase × inheritance ──────────────────────────────────────────────────────────────────────

    private static bool IsInheritedPath(string path) =>
        path == "MasterTerms" || path.StartsWith("Header") || path.StartsWith("Parties") || path.StartsWith("Signatory");

    // A child overlay over a marked base (committed non-inherited data, no inherited data) with inheritance set.
    private static (KVOverlay Overlay, KVSnapshot Base) ChildOverlayForRebase(KVNodeDefinition definition, KVSnapshot parent)
    {
        var childBase = new KVSnapshot { LastCommitId = Guid.NewGuid() };
        childBase.Data["$parent"] = KVValue.FromObject(true);
        childBase.Data["Reference"] = "R0";
        childBase.Data["Status"] = "open";

        var overlay = KVOverlay.Create(childBase.Clone(), "child");
        overlay.SetInheritance(parent, definition.InheritedPrefixes);
        return (overlay, childBase);
    }

    [Fact]
    public void Rebase_of_a_child_leaves_inherited_fields_untouched()
    {
        var definition = BuildDefinition();
        var (overlay, childBase) = ChildOverlayForRebase(definition, ParentSnapshot());

        overlay.TryGet("MasterTerms", out var before);
        before!.Value.Should().Be("Net 30"); // inherited read works before rebase

        // Draft edits a non-inherited field; upstream changed a *different* non-inherited field.
        overlay.Set("Status", "in_review");
        var upstream = new KVCommit { CommitId = Guid.NewGuid(), PreviousCommitId = childBase.LastCommitId, Timestamp = DateTimeOffset.UtcNow };
        upstream.Changes["Reference"] = "R1";
        var target = childBase.Clone();
        target.Apply(upstream);

        overlay.BeginRebase(target, new[] { upstream }).Should().Be(KVRebaseOutcome.CanAutomerge);

        // No conflict touches an inherited path or the child mark; inherited reads still resolve to the parent.
        overlay.Conflicts.Should().NotContain(c => c.Path == "$parent" || IsInheritedPath(c.Path));
        overlay.TryGet("MasterTerms", out var during);
        during!.Value.Should().Be("Net 30");

        overlay.FinishRebase();

        // Non-inherited rebase resolved normally; inherited fields still come from the parent.
        overlay.TryGet("Reference", out var refv); refv!.Value.Should().Be("R1");   // incoming accepted
        overlay.TryGet("Status", out var stv); stv!.Value.Should().Be("in_review"); // our draft preserved
        overlay.TryGet("MasterTerms", out var after); after!.Value.Should().Be("Net 30");
        overlay.TryGet("Header/Region", out var region); region!.Value.Should().Be("EU");
    }

    [Fact]
    public void Rebase_of_a_child_with_a_non_inherited_conflict_resolves_normally()
    {
        var definition = BuildDefinition();
        var (overlay, childBase) = ChildOverlayForRebase(definition, ParentSnapshot());

        // Both sides edit the same non-inherited field → a real conflict.
        overlay.Set("Status", "ours");
        var upstream = new KVCommit { CommitId = Guid.NewGuid(), PreviousCommitId = childBase.LastCommitId, Timestamp = DateTimeOffset.UtcNow };
        upstream.Changes["Status"] = "theirs";
        var target = childBase.Clone();
        target.Apply(upstream);

        overlay.BeginRebase(target, new[] { upstream }).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);

        overlay.Conflicts.Should().Contain(c => c.Path == "Status");
        overlay.Conflicts.Should().NotContain(c => c.Path == "$parent" || IsInheritedPath(c.Path));

        overlay.ResolveConflict("Status", KVConflictResolution.Ours);
        overlay.FinishRebase();

        overlay.TryGet("Status", out var stv); stv!.Value.Should().Be("ours"); // our resolution stuck
        overlay.TryGet("MasterTerms", out var mt); mt!.Value.Should().Be("Net 30"); // inherited survives
    }
}

// ── Test model (manual accessors) ─────────────────────────────────────────────────────────────────
public sealed record ContractProfile : KVValidationProfile
{
    public static ContractProfile Instance { get; } = new();
    private ContractProfile() { }
}

public sealed class Contract : KVRootNode
{
    public string? Reference
    {
        get => GetField<string?>(nameof(Reference));
        set => SetField(nameof(Reference), value);
    }

    public string? Status
    {
        get => GetField<string?>(nameof(Status));
        set => SetField(nameof(Status), value);
    }

    public string? MasterTerms
    {
        get => GetField<string?>(nameof(MasterTerms));
        set => SetField(nameof(MasterTerms), value);
    }

    public ContractHeader Header { get; } = new();

    public KVCollectionNode<ContractParty> Parties { get; } = new();

    public Signatory? Signatory
    {
        get => GetNestedNode<Signatory>(nameof(Signatory));
        set => SetNestedNode(nameof(Signatory), value);
    }

    protected override KVValidationProfile GetValidationProfile() => ContractProfile.Instance;
}

public sealed class ContractHeader : KVFieldGroupNode
{
    public string? Region
    {
        get => GetField<string?>(nameof(Region));
        set => SetField(nameof(Region), value);
    }

    public string? Currency
    {
        get => GetField<string?>(nameof(Currency));
        set => SetField(nameof(Currency), value);
    }
}

public sealed class ContractParty : KVCollectionItemNode
{
    public string? Name
    {
        get => GetField<string?>(nameof(Name));
        set => SetField(nameof(Name), value);
    }
}

public abstract class Signatory : KVNestedNode
{
    public string? Name
    {
        get => GetField<string?>(nameof(Name));
        set => SetField(nameof(Name), value);
    }
}

public sealed class PersonSignatory : Signatory
{
    public string? Title
    {
        get => GetField<string?>(nameof(Title));
        set => SetField(nameof(Title), value);
    }
}

public sealed class CompanySignatory : Signatory
{
    public string? RegistrationNo
    {
        get => GetField<string?>(nameof(RegistrationNo));
        set => SetField(nameof(RegistrationNo), value);
    }
}
