using System;
using System.Collections.Generic;
using x86cc.KVBind.Core.Migrations;

namespace x86cc.KVBind.Core;

public class KVNodeDefinition : KVDefinition
{
    private KVMigration[]? migrationsByVersion;

    // Lazily-built indexes by SubSegmentPath. Definitions are built once via the builder and then reused,
    // so each index is materialized on first lookup and cached thereafter.
    private Dictionary<string, KVFieldDefinition>? fieldsByKey;

    private Dictionary<string, KVNodeDefinition>? nodesByKey;

    private Dictionary<string, KVCollectionDefinition>? collectionsByKey;

    private Dictionary<string, KVNestedNodeDefinition>? nestedNodesByKey;

    private string[]? inheritedPrefixes;

    // Schema migrations registered against the root definition. Only the root populates these;
    // nested/group definitions leave them empty. See KVMigrator.
    public List<KVMigration> Migrations { get; } = new();

    // The newest schema version this binary understands (0 when no migrations are declared). O(1) off the
    // cached sorted array. Compared against a snapshot's SchemaVersion to decide whether data is behind,
    // current, or ahead of the code.
    public int CurrentSchemaVersion
    {
        get
        {
            var sorted = this.MigrationsByVersion;
            return sorted.Length == 0 ? 0 : sorted[^1].ToVersion;
        }
    }

    public List<KVFieldDefinition> Fields { get; } = new();

    public List<KVNodeDefinition> Nodes { get; } = new();

    public List<KVCollectionDefinition> Collections { get; } = new();

    public List<KVNestedNodeDefinition> NestedNodes { get; } = new();

    public List<KVValidationRegistration> ValidationRegistrations { get; } = new();

    public bool? IsResettable { get; set; }

    // Root-only marker on a field group: the whole group is inherited (read-only, parent-sourced) when
    // bound with a parent snapshot. See KVOverlay inheritance.
    public bool IsInherited { get; set; }

    // The SubSegmentPaths of all inherited root members (fields, groups, collections, nested nodes), cached
    // once. The overlay uses these as prefixes to route inherited reads to the parent and block writes.
    public IReadOnlyList<string> InheritedPrefixes
    {
        get
        {
            if (this.inheritedPrefixes is null)
            {
                var prefixes = new List<string>();
                foreach (var fieldDefinition in this.Fields)
                    if (fieldDefinition.IsInherited) prefixes.Add(fieldDefinition.SubSegmentPath);
                foreach (var groupDefinition in this.Nodes)
                    if (groupDefinition.IsInherited) prefixes.Add(groupDefinition.SubSegmentPath);
                foreach (var collectionDefinition in this.Collections)
                    if (collectionDefinition.IsInherited) prefixes.Add(collectionDefinition.SubSegmentPath);
                foreach (var nestedNodeDefinition in this.NestedNodes)
                    if (nestedNodeDefinition.IsInherited) prefixes.Add(nestedNodeDefinition.SubSegmentPath);
                this.inheritedPrefixes = prefixes.ToArray();
            }

            return this.inheritedPrefixes;
        }
    }

    public Func<KVNode, KVNode> GetChildNode { get; init; } = _ => throw new NotImplementedException();

    // Migrations sorted ascending by ToVersion, materialized once and cached (definitions are built then
    // reused, like the field indexes above). Lets the migrator binary-search to the pending tail instead of
    // re-scanning the whole list per aggregate, so already-applied migrations cost nothing at runtime.
    internal KVMigration[] MigrationsByVersion
    {
        get
        {
            if (this.migrationsByVersion is null)
            {
                var sorted = this.Migrations.ToArray();
                Array.Sort(sorted, static (left, right) => left.ToVersion.CompareTo(right.ToVersion));
                this.migrationsByVersion = sorted;
            }

            return this.migrationsByVersion;
        }
    }

    internal List<KVChangeReactionDescriptor> ChangeReactions { get; } = new();

    // Custom patch operations declared on this node with Operation(...): a patch addressed to the node
    // (root "/", a field group, a collection item, an initialized nested node) invokes the mapped method.
    internal Dictionary<string, KVPatchOperationDescriptor> PatchOperations { get; } = new(StringComparer.OrdinalIgnoreCase);

    // Names (upper case) of the custom patch operations a client may send to this node.
    public IReadOnlyCollection<string> OperationNames => this.PatchOperations.Keys;

    internal void AddPatchOperation(KVPatchOperationDescriptor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!this.PatchOperations.TryAdd(operation.Operation, operation))
        {
            throw new InvalidOperationException($"Patch operation '{operation.Operation}' is already registered for '{this.SubSegmentPath}'.");
        }
    }

    // Lookups by SubSegmentPath, replacing allocating O(n) List.Find on warm paths (read, patch resolution,
    // validation, nested-node access). Indexes are lazy and cached; the lists remain authoritative for
    // iteration and declaration order.
    public KVFieldDefinition? FindField(string subSegmentPath)
        => (this.fieldsByKey ??= BuildIndex(this.Fields)).GetValueOrDefault(subSegmentPath);

    public KVNodeDefinition? FindNode(string subSegmentPath)
        => (this.nodesByKey ??= BuildIndex(this.Nodes)).GetValueOrDefault(subSegmentPath);

    public KVCollectionDefinition? FindCollection(string subSegmentPath)
        => (this.collectionsByKey ??= BuildIndex(this.Collections)).GetValueOrDefault(subSegmentPath);

    public KVNestedNodeDefinition? FindNestedNode(string subSegmentPath)
        => (this.nestedNodesByKey ??= BuildIndex(this.NestedNodes)).GetValueOrDefault(subSegmentPath);

    // The builder removes any existing entry before adding, so each SubSegmentPath maps to one definition.
    private static Dictionary<string, T> BuildIndex<T>(List<T> definitions)
        where T : KVDefinition
    {
        var index = new Dictionary<string, T>(definitions.Count, StringComparer.Ordinal);
        foreach (var definition in definitions)
            index[definition.SubSegmentPath] = definition;
        return index;
    }
}
