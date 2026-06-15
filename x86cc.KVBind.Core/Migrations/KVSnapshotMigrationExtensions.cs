using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.Core.Migrations;

public static class KVSnapshotMigrationExtensions
{
    /// <summary>
    /// Mints a new, empty snapshot already stamped at the definition's current schema version — the blessed
    /// way to create a brand-new aggregate so it is born in the newest layout. Set audit fields on the result.
    /// </summary>
    public static KVSnapshot NewSnapshot(this KVNodeDefinition definition)
    {
        System.ArgumentNullException.ThrowIfNull(definition);
        return new KVSnapshot { SchemaVersion = definition.CurrentSchemaVersion };
    }

    /// <summary>
    /// Stamps a freshly-created snapshot with the definition's current schema version, so a brand-new
    /// aggregate is born in the newest layout and is treated as already-migrated. Without this a new snapshot
    /// defaults to version 0 and would be mistaken for pre-migration legacy data — the migrator would try to
    /// replay every migration against data that is already in the newest shape.
    /// </summary>
    public static KVSnapshot StampCurrentSchemaVersion(this KVSnapshot snapshot, KVNodeDefinition definition)
    {
        System.ArgumentNullException.ThrowIfNull(snapshot);
        System.ArgumentNullException.ThrowIfNull(definition);
        snapshot.SchemaVersion = definition.CurrentSchemaVersion;
        return snapshot;
    }
}
