using AwesomeAssertions;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using static x86cc.KVBind.UnitTests.Core.Migrations.DraftMigrationFixture;

namespace x86cc.KVBind.UnitTests.Core.Migrations;

// Draft migration over structure: collection membership and order, nested nodes (initialized, dropped,
// retyped, reset) and the draft edits a migration discards along the way. Each test is user B's draft
// against user A's commit and/or a deployment's migration, as in DraftMigrationTests.
public class DraftMigrationStructureTests
{
    private static readonly KVMigration RenameOccupant =
        KVMigration.Define(2, m => m.RenameNode(KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant"), "Pet"));

    private static readonly KVMigration CatsBecomeDogs = KVMigration.Define(2, m => m.ReplaceNestedType(
        KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").OfType(Cat),
        toType: Dog,
        reshape: r => r.Drop("Whiskers").Set("Breed", _ => "unknown")));

    // ── Collections ─────────────────────────────────────────────────────────────

    [Fact]
    public void An_item_another_user_removed_while_the_draft_edited_it_is_gone()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/k2/Code", "Bob's code");
        CommitAs(committed, "alice", RemoveItem("k2", remaining: ["k1"]));
        var head = Migrated(committed, [RenameOccupant]);

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOccupant]);

        Ids(draft).Should().Equal("k1");
        Read(draft, "Kennels/k2/Code").Should().BeNull();
        Read(draft, "Kennels/k2/Pet/Name").Should().BeNull();
        result.ResolvedConflicts.Should().ContainSingle(c => c.Path == "Kennels/k2" && c.Kind == KVConflictKind.DeleteEdit);
    }

    [Fact]
    public void An_item_another_user_removed_is_kept_with_the_drafts_edit_when_the_draft_wins()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/k2/Code", "Bob's code");
        CommitAs(committed, "alice", RemoveItem("k2", remaining: ["k1"]));
        var head = Migrated(committed, [RenameOccupant]);

        KVMigrator.RebaseDraft(draft, head, [RenameOccupant], KVConflictResolution.Ours);

        Ids(draft).Should().Equal("k1", "k2");
        Read(draft, "Kennels/k2/Code").Should().Be("Bob's code");
    }

    [Fact]
    public void An_item_the_draft_removed_while_another_user_edited_it_comes_back()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        RemoveItem("k2", remaining: ["k1"])(draft);
        CommitAs(committed, "alice", a => a.Set("Kennels/k2/Code", "Alice's code"));
        var head = Migrated(committed, [RenameOccupant]);

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOccupant]);

        Ids(draft).Should().Equal("k1", "k2");
        Read(draft, "Kennels/k2/Code").Should().Be("Alice's code");
        Read(draft, "Kennels/k2/Pet/Name").Should().Be("Rex", "the restored item is in the new layout");
        result.ResolvedConflicts.Should().ContainSingle(c => c.Path == "Kennels/k2" && c.Kind == KVConflictKind.DeleteEdit);
    }

    [Fact]
    public void Items_added_on_both_sides_are_all_kept_and_all_migrated()
    {
        var backfillSize = KVMigration.Define(2, m => m.Backfill(KVTarget.Root.Seg("Kennels").AnyItem().Seg("Size"), _ => "M"));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        AddItem("k4", ["k1", "k2", "k4"])(draft);
        CommitAs(committed, "alice", AddItem("k3", ["k1", "k2", "k3"]));
        var head = Migrated(committed, [backfillSize]);

        var result = KVMigrator.RebaseDraft(draft, head, [backfillSize]);

        Ids(draft).Should().BeEquivalentTo(["k1", "k2", "k3", "k4"]);
        Read(draft, "Kennels/k3/Size").Should().Be("M");
        Read(draft, "Kennels/k4/Size").Should().Be("M");
        Read(draft, "Kennels/k4/Code").Should().Be("K4");
        result.ResolvedConflicts.Should().BeEmpty("adding different items is no conflict");
    }

    [Fact]
    public void The_drafts_order_survives_a_renamed_collection()
    {
        var renameKennels = KVMigration.Define(2, m => m.RenameSegment("Kennels", "Cages"));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/$items", KVValue.FromObject(new[] { "k2", "k1" })); // MOVE
        CommitAs(committed, "alice", a => a.Set("Kennels/k1/Code", "Alice's code"));
        var head = Migrated(committed, [renameKennels]);

        KVMigrator.RebaseDraft(draft, head, [renameKennels]);

        Ids(draft, "Cages").Should().Equal("k2", "k1");
        Read(draft, "Cages/k1/Code").Should().Be("Alice's code");
        draft.Changes.Keys.Should().Equal("Cages/$items");
    }

    // The rebase's membership rule (KVMerge): when both sides changed a collection's membership, the merged
    // list is the committed order, minus the draft's removals, plus the draft's additions — a reorder in the
    // draft does not survive. It is reported as a conflict with a fixed outcome.
    [Fact]
    public void The_committed_order_wins_when_another_user_also_changed_the_membership_and_the_loss_is_reported()
    {
        var renameKennels = KVMigration.Define(2, m => m.RenameSegment("Kennels", "Cages"));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/$items", KVValue.FromObject(new[] { "k2", "k1" })); // MOVE
        CommitAs(committed, "alice", AddItem("k3", ["k1", "k2", "k3"]));
        var head = Migrated(committed, [renameKennels]);

        var result = KVMigrator.RebaseDraft(draft, head, [renameKennels]);

        Ids(draft, "Cages").Should().Equal("k1", "k2", "k3");
        Read(draft, "Cages/k3/Code").Should().Be("K3");
        draft.Changes.Keys.Should().NotContain(key => key.StartsWith("Kennels"));
        var order = result.ResolvedConflicts.Should().ContainSingle().Subject;
        order.Kind.Should().Be(KVConflictKind.Order);
        order.Path.Should().Be("Cages", "in the new layout");
        KVMerge.ExtractItemIds(order.OursValue).Should().Equal("k2", "k1");
    }

    [Fact]
    public void A_lost_order_is_reported_even_when_the_draft_wins_conflicts()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/$items", KVValue.FromObject(new[] { "k2", "k1" }));
        CommitAs(committed, "alice", AddItem("k3", ["k1", "k2", "k3"]));
        var head = Migrated(committed, [RenameOccupant]);

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOccupant], KVConflictResolution.Ours);

        Ids(draft).Should().Equal(["k1", "k2", "k3"], "the order is never the draft's to choose here");
        result.ResolvedConflicts.Should().ContainSingle(c => c.Kind == KVConflictKind.Order && c.Resolution == KVConflictResolution.Theirs);
    }

    [Fact]
    public void An_item_the_draft_added_lands_in_the_renamed_collection()
    {
        var renameKennels = KVMigration.Define(2, m => m.RenameSegment("Kennels", "Cages"));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        AddItem("k4", ["k1", "k2", "k4"])(draft);
        var head = Migrated(committed, [renameKennels]);

        KVMigrator.RebaseDraft(draft, head, [renameKennels]);

        Ids(draft, "Cages").Should().Equal("k1", "k2", "k4");
        Read(draft, "Cages/k4/Code").Should().Be("K4");
        draft.Changes.Keys.Should().BeEquivalentTo("Cages/$items", "Cages/k4/Code");
    }

    // ── Nested nodes ────────────────────────────────────────────────────────────

    [Fact]
    public void A_nested_node_the_draft_dropped_stays_dropped_under_its_new_name()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Remove("Kennels/k2/Occupant"); // DROP
        var head = Migrated(committed, [RenameOccupant]);

        KVMigrator.RebaseDraft(draft, head, [RenameOccupant]);

        draft.Changes.Keys.Should().Equal("Kennels/k2/Pet");
        draft.HasRemovedPath("Kennels/k2/Pet").Should().BeTrue();
        Read(draft, "Kennels/k2/Pet/Name").Should().BeNull();
        Read(draft, "Kennels/k1/Pet/Name").Should().Be("Felix");
    }

    [Fact]
    public void A_nested_node_the_draft_initialized_is_migrated_and_its_dropped_field_reported()
    {
        var committed = ShelterV1();
        RemoveData(committed, "Kennels/k2/Occupant"); // k2 is empty
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/k2/Occupant/$type", Cat); // INIT
        draft.Set("Kennels/k2/Occupant/Name", "Kit");
        draft.Set("Kennels/k2/Occupant/Whiskers", KVValue.FromObject(9));
        var head = Migrated(committed, [CatsBecomeDogs]);

        var result = KVMigrator.RebaseDraft(draft, head, [CatsBecomeDogs]);

        Read(draft, "Kennels/k2/Occupant/$type").Should().Be(Dog);
        Read(draft, "Kennels/k2/Occupant/Name").Should().Be("Kit");
        Read(draft, "Kennels/k2/Occupant/Breed").Should().Be("unknown");
        Read(draft, "Kennels/k2/Occupant/Whiskers").Should().BeNull();
        var removal = result.RemovedByMigration.Should().ContainSingle().Subject;
        removal.Path.Should().Be("Kennels/k2/Occupant/Whiskers");
        removal.Value.Value.Should().Be(9);
        Read(draft, "Kennels/k1/Occupant/$type").Should().Be(Dog, "the committed cat was converted by the head's migration");
    }

    [Fact]
    public void A_nested_node_the_draft_retyped_is_converted_by_the_migration()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        Retype(draft, "Kennels/k2/Occupant", Cat, ("Name", "Kit"), ("Whiskers", 3)); // the dog becomes a cat
        var head = Migrated(committed, [CatsBecomeDogs]);

        var result = KVMigrator.RebaseDraft(draft, head, [CatsBecomeDogs]);

        Read(draft, "Kennels/k2/Occupant/$type").Should().Be(Dog);
        Read(draft, "Kennels/k2/Occupant/Name").Should().Be("Kit");
        Read(draft, "Kennels/k2/Occupant/Breed").Should().Be("unknown", "not the dog's former breed");
        Read(draft, "Kennels/k2/Occupant/Whiskers").Should().BeNull();
        result.RemovedByMigration.Should().ContainSingle(r => r.Path == "Kennels/k2/Occupant/Whiskers");
    }

    [Fact]
    public void Both_sides_retyping_the_same_nested_node_is_a_structural_conflict_the_committed_side_wins()
    {
        var renameContact = KVMigration.Define(2, m => m.RenameSegment("Contact", "ContactInfo"));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        Retype(draft, "Kennels/k1/Occupant", "BIRD", ("Name", "Tweety"), ("Wings", 2));
        CommitAs(committed, "alice", a => Retype(a, "Kennels/k1/Occupant", Dog, ("Name", "Max"), ("Breed", "pug")));
        var head = Migrated(committed, [renameContact]);

        var result = KVMigrator.RebaseDraft(draft, head, [renameContact]);

        result.ResolvedConflicts.Should().ContainSingle(c => c.Path == "Kennels/k1/Occupant" && c.Kind == KVConflictKind.Structural);
        Read(draft, "Kennels/k1/Occupant/$type").Should().Be(Dog);
        Read(draft, "Kennels/k1/Occupant/Breed").Should().Be("pug");
        Read(draft, "Kennels/k1/Occupant/Wings").Should().BeNull("none of the draft's bird is left");
        Read(draft, "ContactInfo/Phone").Should().Be("555-0100");
    }

    [Fact]
    public void Both_sides_retyping_the_same_nested_node_keeps_the_drafts_whole_node_when_the_draft_wins()
    {
        var renameContact = KVMigration.Define(2, m => m.RenameSegment("Contact", "ContactInfo"));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        Retype(draft, "Kennels/k1/Occupant", "BIRD", ("Name", "Tweety"), ("Wings", 2));
        CommitAs(committed, "alice", a => Retype(a, "Kennels/k1/Occupant", Dog, ("Name", "Max"), ("Breed", "pug")));
        var head = Migrated(committed, [renameContact]);

        KVMigrator.RebaseDraft(draft, head, [renameContact], KVConflictResolution.Ours);

        Read(draft, "Kennels/k1/Occupant/$type").Should().Be("BIRD");
        Read(draft, "Kennels/k1/Occupant/Wings").Should().Be(2);
        Read(draft, "Kennels/k1/Occupant/Breed").Should().BeNull("none of the committed dog is left");
    }

    [Fact]
    public void A_nested_node_the_draft_reset_stays_reset_under_its_new_name()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.ResetSubtree("Kennels/k1/Occupant", keepKey: "Kennels/k1/Occupant/$type"); // RESET
        var head = Migrated(committed, [RenameOccupant]);

        KVMigrator.RebaseDraft(draft, head, [RenameOccupant]);

        Read(draft, "Kennels/k1/Pet/$type").Should().Be(Cat, "a reset keeps the type");
        Read(draft, "Kennels/k1/Pet/Name").Should().BeNull();
        draft.Changes.Keys.Should().Equal("Kennels/k1/Pet/Name");
        Read(draft, "Kennels/k2/Pet/Name").Should().Be("Rex");
    }

    // ── Following the draft's edits ─────────────────────────────────────────────

    [Fact]
    public void An_edit_renamed_by_one_migration_and_removed_by_a_later_one_is_reported_where_it_was_removed()
    {
        var migrations = new[]
        {
            KVMigration.Define(2, m => m.RenameSegment("LegacyFlag", "Legacy/Flag")),
            KVMigration.Define(3, m => m.Remove(KVTarget.Root.Seg("Legacy"))),
        };
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("LegacyFlag", "mine");
        var head = Migrated(committed, migrations);

        var result = KVMigrator.RebaseDraft(draft, head, migrations);

        var removal = result.RemovedByMigration.Should().ContainSingle().Subject;
        removal.Path.Should().Be("Legacy/Flag");
        removal.Value.Value.Should().Be("mine");
        removal.Version.Should().Be(3);
        draft.Changes.Should().BeEmpty();
    }

    [Fact]
    public void An_edit_an_overwriting_backfill_derives_from_is_kept()
    {
        var prefixNames = KVMigration.Define(2, m => m.Backfill(
            KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").Seg("Name"),
            view => "Pet:" + view.Absolute(view.Path),
            overwrite: true));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/k1/Occupant/Name", "Tom");
        var head = Migrated(committed, [prefixNames]);

        var result = KVMigrator.RebaseDraft(draft, head, [prefixNames]);

        Read(draft, "Kennels/k1/Occupant/Name").Should().Be("Pet:Tom");
        Read(draft, "Kennels/k2/Occupant/Name").Should().Be("Pet:Rex");
        result.RemovedByMigration.Should().BeEmpty();
    }

    [Fact]
    public void An_edit_an_overwriting_backfill_replaces_with_the_committed_value_is_reported()
    {
        var resetOwner = KVMigration.Define(2, m => m.Backfill(KVTarget.Root.Seg("Owner"), _ => "Shelter Inc", overwrite: true));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        var head = Migrated(committed, [resetOwner]);

        var result = KVMigrator.RebaseDraft(draft, head, [resetOwner]);

        Read(draft, "Owner").Should().Be("Shelter Inc");
        draft.Changes.Should().BeEmpty();
        var removal = result.RemovedByMigration.Should().ContainSingle().Subject;
        removal.Path.Should().Be("Owner");
        removal.Value.Value.Should().Be("Bob's");
    }

    [Fact]
    public void An_edit_a_committed_change_replaced_and_a_migration_then_removed_is_reported_once_as_removed()
    {
        var removeFlag = KVMigration.Define(2, m => m.Remove(KVTarget.Root.Seg("LegacyFlag")));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("LegacyFlag", "Bob's");
        CommitAs(committed, "alice", a => a.Set("LegacyFlag", "Alice's"));
        var head = Migrated(committed, [removeFlag]);

        var result = KVMigrator.RebaseDraft(draft, head, [removeFlag]);

        result.RemovedByMigration.Should().ContainSingle(r => r.Path == "LegacyFlag");
        result.ResolvedConflicts.Should().BeEmpty("the field is gone on both sides");
        draft.Changes.Should().BeEmpty();
    }

    // Value factories run on the head, the base and the draft's view. One that is not deterministic gives
    // each a different value, which shows up as a conflict the user never caused (resolved for the head by
    // default; with Ours, as a draft change). Documented: take such values from a prepare phase.
    [Fact]
    public void A_factory_that_is_not_deterministic_shows_up_as_a_conflict_the_user_never_caused()
    {
        var stampToken = KVMigration.Define(2, m => m.Backfill(KVTarget.Root.Seg("Token"), _ => Guid.NewGuid().ToString()));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        var head = Migrated(committed, [stampToken]);

        var result = KVMigrator.RebaseDraft(draft, head, [stampToken]);

        result.ResolvedConflicts.Should().ContainSingle(c => c.Path == "Token");
        Read(draft, "Token").Should().Be(head.Data["Token"].Value);
        draft.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_factory_reading_prepared_data_gives_the_head_base_and_draft_the_same_value()
    {
        var tokens = new Dictionary<object, string>();
        var stampToken = KVMigration.Define(2, m => m
            .Prepare((subset, _) =>
            {
                foreach (var subject in subset)
                    tokens.TryAdd(subject.Key, Guid.NewGuid().ToString());
                return Task.FromResult<object?>(tokens);
            })
            .Backfill(KVTarget.Root.Seg("Token"), view => view.Context.PreparedAs<Dictionary<object, string>>()[view.Context.Key!]));
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        var head = committed.Clone();
        await KVMigrator.MigrateBatchAsync([new KVMigrationSubject { Key = "shelter", Snapshot = head }], [stampToken]);

        var result = (await KVMigrator.RebaseDraftsAsync([new KVDraftMigrationSubject { Key = "shelter", Draft = draft, Head = head }], [stampToken])).Single();

        result.ResolvedConflicts.Should().BeEmpty();
        draft.Changes.Should().BeEmpty();
        Read(draft, "Token").Should().Be(tokens["shelter"]);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    // REMOVE of a collection item, as the collection node writes it: a tombstone plus the new membership.
    private static Action<KVOverlay> RemoveItem(string id, string[] remaining) => overlay =>
    {
        overlay.Remove("Kennels/" + id);
        overlay.Set("Kennels/$items", KVValue.FromObject(remaining));
    };

    // ADD of an empty kennel with a code.
    private static Action<KVOverlay> AddItem(string id, string[] members) => overlay =>
    {
        overlay.Set("Kennels/$items", KVValue.FromObject(members));
        overlay.Set($"Kennels/{id}/Code", id.ToUpperInvariant());
    };

    // DROP + INIT of another subtype: the old node's data wiped, the new type and its fields written.
    private static void Retype(KVOverlay overlay, string nodePath, string type, params (string Field, object Value)[] fields)
    {
        overlay.ResetSubtree(nodePath);
        overlay.Set(nodePath + "/$type", type);
        foreach (var (field, value) in fields)
            overlay.Set($"{nodePath}/{field}", KVValue.FromObject(value));
    }

    private static void RemoveData(KVSnapshot snapshot, string path)
    {
        foreach (var key in snapshot.Data.Keys.Where(key => KVPath.IsSameOrDescendant(key, path)).ToList())
            snapshot.Data.Remove(key);
    }

    private static List<string> Ids(KVOverlay draft, string collection = "Kennels")
        => draft.TryGet(collection + "/$items", out var items) ? KVMerge.ExtractItemIds(items) : [];
}
