using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;
using static x86cc.KVBind.UnitTests.Core.Migrations.DraftMigrationFixture;

namespace x86cc.KVBind.UnitTests.Core.Migrations;

// A draft is its base snapshot (immutable, a past commit) plus pending changes in that base's layout.
// KVMigrator.RebaseDraft moves it onto the aggregate's migrated head: the draft's changes are migrated and
// merged with whatever else the head holds (other users' commits and the migration commits), conflicts
// resolved with the requested side.
public class DraftMigrationTests
{
    private static readonly KVMigration RenameOwner = KVMigration.Define(2, m => m.RenameSegment("Owner", "Org/Name"));

    [Fact]
    public void A_draft_on_the_pre_migration_head_lands_on_the_migrated_head_with_its_edits_in_the_new_layout()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        draft.Set("Kennels/k1/Occupant/Name", "Tom");
        var migrations = new[]
        {
            KVMigration.Define(2, m => m
                .RenameSegment("Owner", "Org/Name")
                .RenameNode(KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant"), "Pet")),
        };
        var head = Migrated(committed, migrations);

        var result = KVMigrator.RebaseDraft(draft, head, migrations);

        result.Outcome.Should().Be(KVDraftMigrationOutcome.Rebased);
        result.FromVersion.Should().Be(1);
        result.ToVersion.Should().Be(2);
        result.ResolvedConflicts.Should().BeEmpty();
        result.RemovedByMigration.Should().BeEmpty("renamed edits are carried, not removed");
        draft.BaseCommitId.Should().Be(head.LastCommitId);
        draft.Snapshot.SchemaVersion.Should().Be(2);
        draft.Changes.Keys.Should().BeEquivalentTo("Org/Name", "Kennels/k1/Pet/Name");
        Read(draft, "Org/Name").Should().Be("Bob's");
        Read(draft, "Kennels/k1/Pet/Name").Should().Be("Tom");
        Read(draft, "Kennels/k2/Pet/Name").Should().Be("Rex");
    }

    [Fact]
    public void The_draft_base_is_never_modified()
    {
        var committed = ShelterV1();
        var draftBase = committed.Clone();
        var draft = KVOverlay.Create(draftBase, "bob");
        draft.Set("Owner", "Bob's");
        var head = Migrated(committed, [RenameOwner]);

        KVMigrator.RebaseDraft(draft, head, [RenameOwner]);

        draftBase.SchemaVersion.Should().Be(1);
        draftBase.Data["Owner"].Value.Should().Be("Acme");
        draftBase.Data.Should().NotContainKey("Org/Name");
        head.Data["Org/Name"].Value.Should().Be("Acme", "the head is only read");
    }

    // User A and user B branch off the same snapshot; A commits, then a deployment migrates the aggregate.
    // B's draft is two commits behind (A's edit and the migration).
    [Fact]
    public void Another_users_commit_and_a_migration_are_merged_in_one_go_and_theirs_wins_conflicts()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        draft.Set("Kennels/k2/Code", "Bob's code");

        CommitAs(committed, "alice", a =>
        {
            a.Set("Owner", "Alice's");
            a.Set("Contact/Phone", "555-0199");
        });
        var head = Migrated(committed, [RenameOwner]);

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOwner]);

        draft.BaseCommitId.Should().Be(head.LastCommitId);
        Read(draft, "Org/Name").Should().Be("Alice's", "the committed state takes precedence");
        Read(draft, "Contact/Phone").Should().Be("555-0199");
        Read(draft, "Kennels/k2/Code").Should().Be("Bob's code", "non-conflicting edits survive");
        Read(draft, "Owner").Should().BeNull();

        var conflict = result.ResolvedConflicts.Should().ContainSingle().Subject;
        conflict.Path.Should().Be("Org/Name");
        conflict.OursValue!.Value.Should().Be("Bob's");
        conflict.MainValue!.Value.Should().Be("Alice's");
    }

    [Fact]
    public void Conflicts_can_be_resolved_for_the_draft_instead()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        CommitAs(committed, "alice", a => a.Set("Owner", "Alice's"));
        var head = Migrated(committed, [RenameOwner]);

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOwner], KVConflictResolution.Ours);

        Read(draft, "Org/Name").Should().Be("Bob's");
        result.ResolvedConflicts.Should().ContainSingle(c => c.Path == "Org/Name");
    }

    // Replaying A's old-layout commit onto a migrated base would lose this deletion: the base no longer has
    // "Owner", and the migration found nothing to rename. Comparing against the head catches it.
    [Fact]
    public void Another_user_deleting_a_field_that_the_migration_renames_is_a_conflict()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        CommitAs(committed, "alice", a => a.Remove("Owner"));
        var head = Migrated(committed, [RenameOwner]);

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOwner]);

        head.Data.Should().NotContainKey("Org/Name");
        var conflict = result.ResolvedConflicts.Should().ContainSingle().Subject;
        conflict.Path.Should().Be("Org/Name");
        conflict.OursValue!.Value.Should().Be("Bob's");
        conflict.MainValue.Should().BeNull("the head no longer holds the field");
        Read(draft, "Org/Name").Should().BeNull("the deletion was committed first");
    }

    [Fact]
    public void A_draft_edit_that_a_migration_removes_is_reported()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("LegacyFlag", "mine");
        draft.Set("Owner", "Bob's");
        var migrations = new[] { KVMigration.Define(2, m => m.Remove(KVTarget.Root.Seg("LegacyFlag"))) };
        var head = Migrated(committed, migrations);

        var result = KVMigrator.RebaseDraft(draft, head, migrations);

        var removal = result.RemovedByMigration.Should().ContainSingle().Subject;
        removal.Path.Should().Be("LegacyFlag");
        removal.Value.Value.Should().Be("mine");
        removal.Version.Should().Be(2);
        draft.Changes.Keys.Should().Equal("Owner");
    }

    [Fact]
    public void An_item_the_draft_added_is_migrated_too()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Kennels/$items", KVValue.FromObject(new[] { "k1", "k2", "k3" }));
        draft.Set("Kennels/k3/Code", "K3");
        draft.Set("Kennels/k3/Occupant/$type", Dog);
        draft.Set("Kennels/k3/Occupant/Name", "Bolt");
        var migrations = new[]
        {
            KVMigration.Define(2, m => m.Backfill(
                KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").Seg("Vaccinated"), _ => true)),
        };
        var head = Migrated(committed, migrations);

        KVMigrator.RebaseDraft(draft, head, migrations);

        Read(draft, "Kennels/k3/Occupant/Vaccinated").Should().Be(true);
        Read(draft, "Kennels/k3/Occupant/Name").Should().Be("Bolt");
        draft.Changes.Should().NotContainKey("Kennels/k1/Occupant/Vaccinated", "the head already holds the backfill for committed items");
        draft.Changes.Keys.Should().NotContain(key => key.StartsWith("Kennels/$items/"));
    }

    [Fact]
    public void An_item_the_draft_removed_stays_removed_as_one_tombstone()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Remove("Kennels/k2");
        draft.Set("Kennels/$items", KVValue.FromObject(new[] { "k1" }));
        var migrations = new[]
        {
            KVMigration.Define(2, m => m.RenameNode(KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant"), "Pet")),
        };
        var head = Migrated(committed, migrations);

        KVMigrator.RebaseDraft(draft, head, migrations);

        draft.Changes.Keys.Should().BeEquivalentTo("Kennels/k2", "Kennels/$items");
        draft.HasRemovedPath("Kennels/k2").Should().BeTrue();
        Read(draft, "Kennels/k2/Pet/Name").Should().BeNull();
        Read(draft, "Kennels/k1/Pet/Name").Should().Be("Felix");
    }

    [Fact]
    public void Chained_migrations_see_the_drafts_values_in_each_intermediate_layout()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        var migrations = new[]
        {
            RenameOwner,
            KVMigration.Define(3, m => m.Backfill(
                KVTarget.Root.Seg("Org").Seg("Display"), view => view.Sibling("Name") + "!", overwrite: true)),
        };
        var head = Migrated(committed, migrations);

        KVMigrator.RebaseDraft(draft, head, migrations);

        head.Data["Org/Display"].Value.Should().Be("Acme!");
        Read(draft, "Org/Display").Should().Be("Bob's!");
        draft.Snapshot.SchemaVersion.Should().Be(3);
    }

    [Fact]
    public void Committing_the_migrated_draft_continues_the_head()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        var head = Migrated(committed, [RenameOwner]);

        KVMigrator.RebaseDraft(draft, head, [RenameOwner]);
        head.Apply(draft.ToCommit(DateTimeOffset.UtcNow));

        head.Data["Org/Name"].Value.Should().Be("Bob's");
        head.Data.Should().NotContainKey("Owner");
    }

    [Fact]
    public void A_draft_already_at_the_heads_schema_version_is_left_alone()
    {
        var committed = ShelterV1();
        var head = Migrated(committed, [RenameOwner]);
        var draftBase = head.Clone();
        var draft = KVOverlay.Create(draftBase, "bob");
        draft.Set("Org/Name", "Bob's");
        CommitAs(head, "alice", a => a.Set("Contact/Phone", "555-0199"));

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOwner]);

        result.Outcome.Should().Be(KVDraftMigrationOutcome.AlreadyCurrent);
        draft.Snapshot.Should().BeSameAs(draftBase, "catching up on ordinary commits is the interactive rebase");
        draft.Changes.Keys.Should().Equal("Org/Name");
    }

    [Fact]
    public void A_rebase_in_progress_from_the_old_layout_is_abandoned_and_redone()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        var aliceCommit = CommitAs(committed, "alice", a => a.Set("Owner", "Alice's"));
        draft.BeginRebase(committed, [aliceCommit]).Should().Be(KVRebaseOutcome.HasUnresolvedConflicts);
        var head = Migrated(committed, [RenameOwner]);

        var result = KVMigrator.RebaseDraft(draft, head, [RenameOwner]);

        result.AbandonedRebase.Should().BeTrue();
        draft.IsRebasing.Should().BeFalse();
        draft.BaseCommitId.Should().Be(head.LastCommitId);
        Read(draft, "Org/Name").Should().Be("Alice's");
    }

    [Fact]
    public void The_head_must_be_migrated_first()
    {
        var definition = ShelterDefinition();
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");

        var act = () => KVMigrator.RebaseDraft(draft, committed, definition);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Migrate the snapshot first*");
    }

    [Fact]
    public void A_draft_newer_than_the_head_is_rejected()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(Migrated(committed, [RenameOwner]), "bob");

        var act = () => KVMigrator.RebaseDraft(draft, committed, Array.Empty<KVMigration>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*newer than the head*");
    }

    [Fact]
    public void Only_ours_or_theirs_can_resolve_conflicts()
    {
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        var head = Migrated(committed, [RenameOwner]);

        var act = () => KVMigrator.RebaseDraft(draft, head, [RenameOwner], KVConflictResolution.Custom);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_registered_migrations_are_used_with_a_definition()
    {
        var definition = ShelterDefinition();
        var committed = ShelterV1();
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Owner", "Bob's");
        var head = committed.Clone();
        KVMigrator.Migrate(head, definition);

        var result = KVMigrator.RebaseDraft(draft, head, definition);

        result.Outcome.Should().Be(KVDraftMigrationOutcome.Rebased);
        Read(draft, "Org/Name").Should().Be("Bob's");
    }

    [Fact]
    public async Task A_batch_runs_each_prepare_once_over_the_drafts_that_need_it()
    {
        var calls = new List<IReadOnlyList<object>>();
        var ownersSeen = new List<object?>();
        var migrations = new[]
        {
            KVMigration.Define(2, m => m
                .Prepare((subset, _) =>
                {
                    calls.Add(subset.Select(subject => subject.Key).ToList());
                    ownersSeen.AddRange(subset.Select(subject => subject.Snapshot.Data["Owner"].Value));
                    return Task.FromResult<object?>(subset.ToDictionary(
                        subject => subject.Key,
                        subject => "Region of " + subject.Snapshot.Data["Owner"].Value));
                })
                .Backfill(KVTarget.Root.Seg("Region"), view => view.Context.PreparedAs<Dictionary<object, string>>()[view.Context.Key!])),
        };

        var first = ShelterV1();
        var firstDraft = KVOverlay.Create(first.Clone(), "bob");
        firstDraft.Set("Owner", "Bob's");
        var second = ShelterV1();
        var secondDraft = KVOverlay.Create(second.Clone(), "carol");
        var current = MigratedWithContext(ShelterV1(), migrations, "current");
        var currentDraft = KVOverlay.Create(current.Clone(), "dave");

        var firstHead = MigratedWithContext(first, migrations, "first");
        var secondHead = MigratedWithContext(second, migrations, "second");
        calls.Clear(); // the heads' own migrations called it too
        ownersSeen.Clear();

        var results = await KVMigrator.RebaseDraftsAsync(
            [
                new KVDraftMigrationSubject { Key = "first", Draft = firstDraft, Head = firstHead },
                new KVDraftMigrationSubject { Key = "second", Draft = secondDraft, Head = secondHead },
                new KVDraftMigrationSubject { Key = "current", Draft = currentDraft, Head = current },
            ],
            migrations);

        calls.Should().ContainSingle().Which.Should().Equal("first", "second");
        results.Select(result => result.Key).Should().Equal("first", "second", "current");
        results[2].Outcome.Should().Be(KVDraftMigrationOutcome.AlreadyCurrent);
        ownersSeen.Should().Equal(["Bob's", "Acme"], "the prepare phase sees each draft's view");
        Read(firstDraft, "Region").Should().Be("Region of Acme", "base and draft share the prepared data, so the head's value stands");
        Read(secondDraft, "Region").Should().Be("Region of Acme");
        firstDraft.BaseCommitId.Should().Be(firstHead.LastCommitId);
    }

    [Fact]
    public void A_child_draft_never_migrates_its_inherited_members()
    {
        var builder = new KVBindBuilder<x86cc.KVBind.UnitTests.Core.Inheritance.Contract>();
        builder.Field(x => x.Reference);
        builder.Field(x => x.MasterTerms, f => f.Inherited());
        builder.Migration(2, m => m
            .Backfill(KVTarget.Root.Seg("MasterTerms"), _ => "MIGRATED-TERMS")
            .Backfill(KVTarget.Root.Seg("Reference"), _ => "MIGRATED-REF"));
        var definition = builder.Build();

        var committed = new KVSnapshot { SchemaVersion = 1, LastCommitId = Guid.NewGuid() };
        committed.Data["$parent"] = KVValue.FromObject(true);
        var draft = KVOverlay.Create(committed.Clone(), "bob");
        draft.Set("Reference", "Bob's");
        var head = committed.Clone();
        KVMigrator.Migrate(head, definition);

        KVMigrator.RebaseDraft(draft, head, definition);

        draft.Changes.Keys.Should().Equal("Reference");
        Read(draft, "MasterTerms").Should().BeNull();
        Read(draft, "Reference").Should().Be("Bob's", "the draft's own value is kept, not overwritten by a non-overwriting backfill");
    }

    [Fact]
    public void A_wildcard_never_matches_a_collections_reserved_items_key()
    {
        var snapshot = ShelterV1();

        KVMigrator.Migrate(snapshot, [KVMigration.Define(2, m => m.Backfill(
            KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").Seg("Vaccinated"), _ => true))]);

        snapshot.Data.Keys.Should().NotContain(key => key.StartsWith("Kennels/$items/"));
        snapshot.Data["Kennels/k1/Occupant/Vaccinated"].Value.Should().Be(true);
    }

    private static KVNodeDefinition ShelterDefinition()
    {
        var builder = new KVBindBuilder<ShelterRoot>();
        builder.Field(x => x.Owner);
        builder.Migration(2, m => m.RenameSegment("Owner", "Org/Name"));
        return builder.Build();
    }
}
