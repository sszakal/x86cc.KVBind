using AwesomeAssertions;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Migrations;

public class MigrationFieldViewTests
{
    [Fact]
    public void Sibling_WhenFieldIsNested_ReadsFieldUnderSameParent()
    {
        var snapshot = new KVSnapshot { SchemaVersion = 1 };
        snapshot.Data["Kennels/$items"] = KVValue.FromObject(new[] { "k1" });
        snapshot.Data["Kennels/k1/First"] = "Ada";
        snapshot.Data["Kennels/k1/Last"] = "Lovelace";

        KVMigrator.Migrate(snapshot, new[]
        {
            KVMigration.Define(2, m => m.Backfill(
                KVTarget.Root.Seg("Kennels").AnyItem().Seg("Full"),
                view => view.Sibling("First") + " " + view.Sibling("Last"))),
        });

        snapshot.Data["Kennels/k1/Full"].Value.Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Sibling_WhenFieldIsAtRoot_ReadsRootLevelField()
    {
        var snapshot = new KVSnapshot { SchemaVersion = 1 };
        snapshot.Data["Owner"] = "Acme";

        KVMigrator.Migrate(snapshot, new[]
        {
            KVMigration.Define(2, m => m.Backfill(KVTarget.Root.Seg("OwnerCopy"), view => view.Sibling("Owner"))),
        });

        snapshot.Data["OwnerCopy"].Value.Should().Be("Acme");
    }

    [Fact]
    public void Sibling_WhenSiblingIsAbsent_ReturnsNull()
    {
        var snapshot = new KVSnapshot { SchemaVersion = 1 };
        object? observed = "sentinel";

        KVMigrator.Migrate(snapshot, new[]
        {
            KVMigration.Define(2, m => m.Backfill(KVTarget.Root.Seg("Flag"), view =>
            {
                observed = view.Sibling("Missing");
                return true;
            })),
        });

        observed.Should().BeNull();
    }
}
