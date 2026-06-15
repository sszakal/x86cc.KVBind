using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Migrations;

namespace x86cc.KVBind.Core;

public class KVNodeDefinition : KVDefinition
{
    private KVMigration[]? _migrationsByVersion;

    // Schema migrations registered against the root definition. Only the root populates these;
    // nested/group definitions leave them empty. See KVMigrator.
    public List<KVMigration> Migrations { get; } = new();

    // Migrations sorted ascending by ToVersion, materialized once and cached (definitions are built then
    // reused, like the field indexes above). Lets the migrator binary-search to the pending tail instead of
    // re-scanning the whole list per aggregate, so already-applied migrations cost nothing at runtime.
    internal KVMigration[] MigrationsByVersion
    {
        get
        {
            if (_migrationsByVersion is null)
            {
                var sorted = Migrations.ToArray();
                Array.Sort(sorted, static (left, right) => left.ToVersion.CompareTo(right.ToVersion));
                _migrationsByVersion = sorted;
            }

            return _migrationsByVersion;
        }
    }

    // The newest schema version this binary understands (0 when no migrations are declared). O(1) off the
    // cached sorted array. Compared against a snapshot's SchemaVersion to decide whether data is behind,
    // current, or ahead of the code.
    public int CurrentSchemaVersion
    {
        get
        {
            var sorted = MigrationsByVersion;
            return sorted.Length == 0 ? 0 : sorted[^1].ToVersion;
        }
    }

    // Lazily-built indexes by SubSegmentPath. Definitions are built once via the builder and then reused,
    // so each index is materialized on first lookup and cached thereafter.
    private Dictionary<string, KVFieldDefinition>? _fieldsByKey;
    private Dictionary<string, KVNodeDefinition>? _nodesByKey;
    private Dictionary<string, KVCollectionDefinition>? _collectionsByKey;
    private Dictionary<string, KVNestedNodeDefinition>? _nestedNodesByKey;

    public List<KVFieldDefinition> Fields { get; } = new();
    public List<KVNodeDefinition> Nodes { get; } = new();
    public List<KVCollectionDefinition> Collections { get; } = new();
    public List<KVNestedNodeDefinition> NestedNodes { get; } = new();
    public List<KVValidationRegistration> ValidationRegistrations { get; } = new();
    internal List<KVChangeReactionDescriptor> ChangeReactions { get; } = new();
    public bool? IsResettable { get; set; }
    public Func<KVNode, KVNode> GetChildNode { get; init; } = _ => throw new NotImplementedException();

    // Lookups by SubSegmentPath, replacing allocating O(n) List.Find on warm paths (read, patch resolution,
    // validation, nested-node access). Indexes are lazy and cached; the lists remain authoritative for
    // iteration and declaration order.
    public KVFieldDefinition? FindField(string subSegmentPath)
        => (_fieldsByKey ??= BuildIndex(Fields)).GetValueOrDefault(subSegmentPath);

    public KVNodeDefinition? FindNode(string subSegmentPath)
        => (_nodesByKey ??= BuildIndex(Nodes)).GetValueOrDefault(subSegmentPath);

    public KVCollectionDefinition? FindCollection(string subSegmentPath)
        => (_collectionsByKey ??= BuildIndex(Collections)).GetValueOrDefault(subSegmentPath);

    public KVNestedNodeDefinition? FindNestedNode(string subSegmentPath)
        => (_nestedNodesByKey ??= BuildIndex(NestedNodes)).GetValueOrDefault(subSegmentPath);

    // The builder removes any existing entry before adding, so each SubSegmentPath maps to one definition.
    private static Dictionary<string, T> BuildIndex<T>(List<T> definitions) where T : KVDefinition
    {
        var index = new Dictionary<string, T>(definitions.Count, StringComparer.Ordinal);
        foreach (var definition in definitions)
            index[definition.SubSegmentPath] = definition;
        return index;
    }
}
