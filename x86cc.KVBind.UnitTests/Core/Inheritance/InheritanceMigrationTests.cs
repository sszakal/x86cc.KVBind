using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Inheritance;

// A child must never migrate the fields it always reads from its parent. The parent (a master) migrates
// those fields normally, and the child sees the result through the inheritance read-through.
public class InheritanceMigrationTests
{
    private static KVNodeDefinition DefinitionWithBackfills()
    {
        var builder = new KVBindBuilder<Contract>();
        builder.Field(x => x.Reference);
        builder.Field(x => x.MasterTerms, f => f.Inherited());
        builder.Migration(2, m => m
            .Backfill(KVTarget.Root.Seg("MasterTerms"), _ => "MIGRATED-TERMS") // inherited
            .Backfill(KVTarget.Root.Seg("Reference"), _ => "MIGRATED-REF"));   // non-inherited
        return builder.Build();
    }

    [Fact]
    public void Master_migration_backfills_both_inherited_and_non_inherited_fields()
    {
        var definition = DefinitionWithBackfills();
        var master = new KVSnapshot { SchemaVersion = 1 };

        KVMigrator.Migrate(master, definition);

        master.Data["MasterTerms"].Value.Should().Be("MIGRATED-TERMS");
        master.Data["Reference"].Value.Should().Be("MIGRATED-REF");
        master.SchemaVersion.Should().Be(2);
    }

    [Fact]
    public void Child_migration_skips_the_inherited_backfill_but_applies_the_rest()
    {
        var definition = DefinitionWithBackfills();
        var child = new KVSnapshot { SchemaVersion = 1 };
        child.Data["$parent"] = KVValue.FromObject(true); // child mark

        KVMigrator.Migrate(child, definition);

        child.Data.Should().NotContainKey("MasterTerms");          // inherited — never backfilled into a child
        child.Data["Reference"].Value.Should().Be("MIGRATED-REF"); // non-inherited — applied
        child.SchemaVersion.Should().Be(2);                        // version still advances
    }

    [Fact]
    public async Task Batch_migration_skips_the_inherited_backfill_only_for_children()
    {
        var definition = DefinitionWithBackfills();
        var master = new KVMigrationSubject { Key = "M", Snapshot = new KVSnapshot { SchemaVersion = 1 } };
        var childSnapshot = new KVSnapshot { SchemaVersion = 1 };
        childSnapshot.Data["$parent"] = KVValue.FromObject(true);
        var child = new KVMigrationSubject { Key = "C", Snapshot = childSnapshot };

        await KVMigrator.MigrateBatchAsync(new[] { master, child }, definition);

        // Master got the inherited backfill; child did not — but both got the non-inherited one.
        master.Snapshot.Data["MasterTerms"].Value.Should().Be("MIGRATED-TERMS");
        master.Snapshot.Data["Reference"].Value.Should().Be("MIGRATED-REF");
        child.Snapshot.Data.Should().NotContainKey("MasterTerms");
        child.Snapshot.Data["Reference"].Value.Should().Be("MIGRATED-REF");
        child.Snapshot.SchemaVersion.Should().Be(2);
    }
}
