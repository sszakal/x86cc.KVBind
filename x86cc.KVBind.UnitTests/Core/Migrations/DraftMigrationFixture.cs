using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Migrations;

// Shared data and steps for the draft migration tests: a v1 shelter, a deployment's migration of it, and
// another user's commit on top of it.
internal static class DraftMigrationFixture
{
    public const string Cat = "CAT";

    public const string Dog = "DOG";

    // A v1 shelter: kennel k1 holds a Cat, kennel k2 holds a Dog.
    public static KVSnapshot ShelterV1()
    {
        var snapshot = new KVSnapshot { SchemaVersion = 1, LastCommitId = Guid.NewGuid() };
        snapshot.Data["Owner"] = "Acme";
        snapshot.Data["LegacyFlag"] = "obsolete";
        snapshot.Data["Contact/Phone"] = "555-0100";
        snapshot.Data["Kennels/$items"] = KVValue.FromObject(new[] { "k1", "k2" });
        snapshot.Data["Kennels/k1/Code"] = "K1";
        snapshot.Data["Kennels/k1/Occupant/$type"] = Cat;
        snapshot.Data["Kennels/k1/Occupant/Name"] = "Felix";
        snapshot.Data["Kennels/k2/Code"] = "K2";
        snapshot.Data["Kennels/k2/Occupant/$type"] = Dog;
        snapshot.Data["Kennels/k2/Occupant/Name"] = "Rex";
        return snapshot;
    }

    // The aggregate's head after the deployment's migration: a migrated copy of the committed snapshot.
    public static KVSnapshot Migrated(KVSnapshot committed, IEnumerable<KVMigration> migrations)
    {
        var head = committed.Clone();
        KVMigrator.Migrate(head, migrations);
        return head;
    }

    public static KVSnapshot MigratedWithContext(KVSnapshot committed, IReadOnlyList<KVMigration> migrations, string key)
    {
        var head = committed.Clone();
        KVMigrator.MigrateBatchAsync([new KVMigrationSubject { Key = key, Snapshot = head }], migrations).GetAwaiter().GetResult();
        return head;
    }

    // Another user's draft over the current snapshot, committed onto it.
    public static KVCommit CommitAs(KVSnapshot snapshot, string user, Action<KVOverlay> edit)
    {
        var overlay = KVOverlay.Create(snapshot.Clone(), user);
        edit(overlay);
        var commit = overlay.ToCommit(DateTimeOffset.UtcNow);
        snapshot.Apply(commit);
        return commit;
    }

    public static object? Read(KVOverlay draft, string path) => draft.TryGet(path, out var value) ? value?.Value : null;
}
