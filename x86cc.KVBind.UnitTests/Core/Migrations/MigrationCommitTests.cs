using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Migrations;

public class MigrationCommitTests
{
    [Fact]
    public async Task Batch_runs_prepare_once_over_only_the_subset_that_needs_each_migration()
    {
        // A is behind at v1; B is already at v2; C is fully current at v3.
        var a = new KVMigrationSubject { Key = "A", Snapshot = new KVSnapshot { SchemaVersion = 1 } };
        var b = new KVMigrationSubject { Key = "B", Snapshot = new KVSnapshot { SchemaVersion = 2 } };
        var c = new KVMigrationSubject { Key = "C", Snapshot = new KVSnapshot { SchemaVersion = 3 } };

        var prepareV2Subsets = new List<string[]>();
        var prepareV3Subsets = new List<string[]>();

        var migrations = new[]
        {
            KVMigration.Define(2, m => m
                // Batch-fetch a per-aggregate amount, then map it into each aggregate.
                .Prepare((subset, _) =>
                {
                    prepareV2Subsets.Add(subset.Select(s => (string)s.Key).ToArray());
                    var data = subset.ToDictionary(s => (string)s.Key, s => (object?)((string)s.Key + "-amount"));
                    return Task.FromResult<object?>(data);
                })
                .Backfill(KVTarget.Root.Seg("Amount"),
                    view => view.Context.PreparedAs<Dictionary<string, object?>>()[view.Context.KeyAs<string>()])),
            KVMigration.Define(3, m => m
                .Prepare((subset, _) =>
                {
                    prepareV3Subsets.Add(subset.Select(s => (string)s.Key).ToArray());
                    return Task.FromResult<object?>(null);
                })
                .Backfill(KVTarget.Root.Seg("Reviewed"), _ => true)),
        };

        var results = await KVMigrator.MigrateBatchAsync(new[] { a, b, c }, migrations);

        // Prepare for v2 saw only A (B already v2, C already v3). Prepare for v3 saw A and B (not C).
        prepareV2Subsets.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { "A" });
        prepareV3Subsets.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { "A", "B" });

        // Commit counts per aggregate: A -> [2,3], B -> [3], C -> none.
        Commits(results, "A").Select(x => x.MigrationToVersion).Should().Equal(2, 3);
        Commits(results, "B").Select(x => x.MigrationToVersion).Should().Equal(3);
        Commits(results, "C").Should().BeEmpty();

        // Snapshots advanced in place; prepared data was mapped per aggregate.
        a.Snapshot.SchemaVersion.Should().Be(3);
        a.Snapshot.Data["Amount"].Value.Should().Be("A-amount");
        a.Snapshot.Data["Reviewed"].Value.Should().Be(true);
        b.Snapshot.SchemaVersion.Should().Be(3);
        b.Snapshot.Data.Should().NotContainKey("Amount"); // B skipped migration 2
        c.Snapshot.SchemaVersion.Should().Be(3);

        static IReadOnlyList<KVCommit> Commits(IReadOnlyList<KVMigrationResult> results, string key)
            => results.Single(r => (string)r.Key == key).Commits;
    }

    private sealed class MigrationRootNode : KVRootNode
    {
        public string? Title
        {
            get => GetField<string?>("Title");
            set => SetField("Title", value);
        }
    }

    [Fact]
    public void Builder_registers_migrations_on_the_root_definition()
    {
        var builder = new KVBindBuilder<MigrationRootNode>();
        builder.Field(x => x.Title);
        builder.Migration(2, m => m.Backfill(KVTarget.Root.Seg("Status"), _ => "draft"));
        builder.Migration(3, m => m.Remove(KVTarget.Root.Seg("Legacy")));
        var definition = builder.Build();

        definition.Migrations.Should().HaveCount(2);
        definition.CurrentSchemaVersion.Should().Be(3);

        var snapshot = new KVSnapshot { SchemaVersion = 1 };
        snapshot.Data["Legacy"] = "x";

        var commits = KVMigrator.BuildCommits(snapshot, definition);

        commits.Select(commit => commit.MigrationToVersion).Should().Equal(2, 3);
    }

    [Fact]
    public void Builder_rejects_duplicate_migration_versions()
    {
        var builder = new KVBindBuilder<MigrationRootNode>();
        builder.Migration(2, _ => { });

        var act = () => builder.Migration(2, _ => { });

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void New_aggregate_stamped_at_current_version_runs_no_migrations()
    {
        var builder = new KVBindBuilder<MigrationRootNode>();
        builder.Field(x => x.Title);
        builder.Migration(2, m => m.Backfill(KVTarget.Root.Seg("A"), _ => "a"));
        builder.Migration(3, m => m.Backfill(KVTarget.Root.Seg("B"), _ => "b"));
        var definition = builder.Build();

        // A brand-new aggregate, minted via the definition factory, is born stamped at the current version.
        var fresh = definition.NewSnapshot();
        fresh.SchemaVersion.Should().Be(3);

        // It is already current → no migration commits, and the backfills never touch its data.
        KVMigrator.BuildCommits(fresh, definition).Should().BeEmpty();
        KVMigrator.Migrate(fresh, definition).Should().BeEmpty();
        fresh.Data.Should().NotContainKey("A");
        fresh.Data.Should().NotContainKey("B");

        // Contrast / footgun: an unstamped snapshot defaults to version 0 and is mistaken for legacy data,
        // so every migration would replay against it. This is exactly what stamping prevents.
        var unstamped = new KVSnapshot();
        KVMigrator.BuildCommits(unstamped, definition)
            .Select(commit => commit.MigrationToVersion).Should().Equal(2, 3);
    }

    [Fact]
    public void Up_to_date_snapshot_produces_no_commits_without_running_migrations()
    {
        var builder = new KVBindBuilder<MigrationRootNode>();
        // An already-applied migration whose step would throw if it were ever executed again.
        builder.Migration(2, m => m.Backfill(KVTarget.Root.Seg("X"),
            _ => throw new InvalidOperationException("applied migration must not run")));
        builder.Migration(3, m => m.Backfill(KVTarget.Root.Seg("Y"), _ => "y"));
        var definition = builder.Build();

        // Snapshot already at the newest version: the fast path returns empty and never touches the list.
        var current = new KVSnapshot { SchemaVersion = 3 };
        KVMigrator.BuildCommits(current, definition).Should().BeEmpty();

        // Snapshot one behind: only the v3 tail runs; the applied v2 step is never invoked (no throw).
        var behind = new KVSnapshot { SchemaVersion = 2 };
        var commits = KVMigrator.Migrate(behind, definition);

        commits.Select(commit => commit.MigrationToVersion).Should().Equal(3);
        behind.Data["Y"].Value.Should().Be("y");
        behind.SchemaVersion.Should().Be(3);
    }

    // A v1-shaped claim: a polymorphic damaged-items collection and a polymorphic Claimant nested node.
    private static KVSnapshot V1Snapshot()
    {
        var snapshot = new KVSnapshot { SchemaVersion = 1, LastCommitId = Guid.NewGuid() };
        snapshot.Data["ClaimNumber"] = "C-1";
        snapshot.Data["Status"] = "draft";
        snapshot.Data["DamagedItems/$items"] = KVValue.FromObject(new[] { "g1", "g2" });
        snapshot.Data["DamagedItems/g1/$type"] = "DamagedItem";
        snapshot.Data["DamagedItems/g1/Category"] = "structural";
        snapshot.Data["DamagedItems/g1/LegacyNote"] = "old";
        snapshot.Data["DamagedItems/g2/$type"] = "VehicleItem";
        snapshot.Data["DamagedItems/g2/Category"] = "mechanical";
        snapshot.Data["Claimant/$type"] = "PERSON";
        snapshot.Data["Claimant/Name"] = "Alice";
        return snapshot;
    }

    [Fact]
    public void Each_pending_migration_becomes_its_own_chained_commit()
    {
        var snapshot = V1Snapshot();

        var migrations = new[]
        {
            KVMigration.Define(2, m => m
                // Rename Category -> DamageCategory, but ONLY for DamagedItem-typed items (not VehicleItem).
                .RenameField(KVTarget.Root.Seg("DamagedItems").AnyItem(ofType: "DamagedItem").Seg("Category"),
                    "DamageCategory")),
            KVMigration.Define(3, m => m
                // A removed field, a backfilled new field, and a nested-node-subtype field rename.
                .Remove(KVTarget.Root.Seg("DamagedItems").AnyItem().Seg("LegacyNote"))
                .Backfill(KVTarget.Root.Seg("Policy").Seg("Deductible"), _ => 500m)
                .RenameField(KVTarget.Root.Seg("Claimant").OfType("PERSON").Seg("Name"), "FullName")),
        };

        var commits = KVMigrator.BuildCommits(snapshot, migrations);

        commits.Should().HaveCount(2);
        commits[0].MigrationToVersion.Should().Be(2);
        commits[1].MigrationToVersion.Should().Be(3);
        // Chained: the second continues the first.
        commits[1].PreviousCommitId.Should().Be(commits[0].CommitId);
        commits[0].PreviousCommitId.Should().Be(snapshot.LastCommitId);
        // Source snapshot is untouched until we apply.
        snapshot.SchemaVersion.Should().Be(1);

        // Apply and check the resulting v3 shape.
        foreach (var commit in commits)
            snapshot.Apply(commit);

        snapshot.SchemaVersion.Should().Be(3);

        // Polymorphic leaf rename hit only the DamagedItem-typed item.
        snapshot.Data.Should().ContainKey("DamagedItems/g1/DamageCategory");
        snapshot.Data.Should().NotContainKey("DamagedItems/g1/Category");
        snapshot.Data["DamagedItems/g1/DamageCategory"].Value.Should().Be("structural");
        snapshot.Data.Should().ContainKey("DamagedItems/g2/Category"); // VehicleItem left alone
        snapshot.Data.Should().NotContainKey("DamagedItems/g2/DamageCategory");

        // Remove + backfill + nested-subtype rename.
        snapshot.Data.Should().NotContainKey("DamagedItems/g1/LegacyNote");
        snapshot.Data["Policy/Deductible"].Value.Should().Be(500m);
        snapshot.Data["Claimant/FullName"].Value.Should().Be("Alice");
        snapshot.Data.Should().NotContainKey("Claimant/Name");
    }

    [Fact]
    public void RenameSegment_rewrites_the_whole_subtree_carrying_item_ids()
    {
        var snapshot = V1Snapshot();

        var migration = KVMigration.Define(2, m => m.RenameSegment("DamagedItems", "Damages"));

        KVMigrator.Migrate(snapshot, new[] { migration });

        snapshot.Data.Should().ContainKey("Damages/$items");
        snapshot.Data.Should().ContainKey("Damages/g1/$type");
        snapshot.Data.Should().ContainKey("Damages/g1/Category");
        snapshot.Data.Keys.Should().NotContain(key => key.StartsWith("DamagedItems/"));
    }

    [Fact]
    public void A_noop_backfill_still_produces_a_commit_that_bumps_the_version()
    {
        var snapshot = V1Snapshot();

        // Status is already "draft" and we don't overwrite, so the delta is empty — but the version must move.
        var migration = KVMigration.Define(2, m => m
            .Backfill(KVTarget.Root.Seg("Status"), _ => "draft"));

        var commits = KVMigrator.BuildCommits(snapshot, new[] { migration });

        commits.Should().HaveCount(1);
        commits[0].Changes.Should().BeEmpty();
        commits[0].MigrationToVersion.Should().Be(2);

        snapshot.Apply(commits[0]);
        snapshot.SchemaVersion.Should().Be(2);
    }

    [Fact]
    public void Already_applied_migrations_are_skipped()
    {
        var snapshot = V1Snapshot(); // SchemaVersion = 1

        var migrations = new[]
        {
            KVMigration.Define(1, m => m.Remove(KVTarget.Root.Seg("ClaimNumber"))), // already applied
            KVMigration.Define(2, m => m.Backfill(KVTarget.Root.Seg("Priority"), _ => "medium")),
        };

        var commits = KVMigrator.BuildCommits(snapshot, migrations);

        commits.Should().HaveCount(1);
        commits[0].MigrationToVersion.Should().Be(2);
    }
}
