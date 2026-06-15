using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Migrations;

// Covers every migration scenario from the design discussion against a model with a collection whose items
// each hold a polymorphic nested node (Animal base → Cat / Dog).
//
//   ShelterRoot
//     Owner            (root field)
//     LegacyFlag       (root field, slated for removal)
//     Contact          (field group) → Phone
//     Kennels          (collection)
//       {id}/Code      (item field)
//       {id}/Occupant  (nested node: AnimalNode → CatNode | DogNode)
//                        Name      — common to both types
//                        Whiskers  — Cat only
//                        Breed     — Dog only
public class MigrationScenarioTests
{
    private const string Cat = "CAT";
    private const string Dog = "DOG";

    // A v1 shelter: kennel k1 holds a Cat, kennel k2 holds a Dog.
    private static KVSnapshot ShelterV1()
    {
        var snapshot = new KVSnapshot { SchemaVersion = 1, LastCommitId = Guid.NewGuid() };
        snapshot.Data["Owner"] = "Acme";
        snapshot.Data["LegacyFlag"] = "obsolete";
        snapshot.Data["Contact/Phone"] = "555-0100";

        snapshot.Data["Kennels/$items"] = KVValue.FromObject(new[] { "k1", "k2" });

        snapshot.Data["Kennels/k1/Code"] = "K1";
        snapshot.Data["Kennels/k1/Occupant/$type"] = Cat;
        snapshot.Data["Kennels/k1/Occupant/Name"] = "Felix";
        snapshot.Data["Kennels/k1/Occupant/Whiskers"] = KVValue.FromObject(12);

        snapshot.Data["Kennels/k2/Code"] = "K2";
        snapshot.Data["Kennels/k2/Occupant/$type"] = Dog;
        snapshot.Data["Kennels/k2/Occupant/Name"] = "Rex";
        snapshot.Data["Kennels/k2/Occupant/Breed"] = "lab";
        return snapshot;
    }

    private static object? V(KVSnapshot s, string path) => s.Data.TryGetValue(path, out var v) ? v?.Value : null;

    // ── Scenario: field removed ───────────────────────────────────────────────
    [Fact]
    public void Field_removed()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[] { KVMigration.Define(2, m => m.Remove(KVTarget.Root.Seg("LegacyFlag"))) });

        s.Data.Should().NotContainKey("LegacyFlag");
        s.SchemaVersion.Should().Be(2);
    }

    // ── Scenario: field added + backfill (a new field with no backfill needs no migration) ────
    [Fact]
    public void Field_added_with_backfill()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[] { KVMigration.Define(2, m => m.Backfill(KVTarget.Root.Seg("Region"), _ => "EU")) });

        V(s, "Region").Should().Be("EU");
    }

    // ── Scenario: field moved to a different parent (/Owner → /Org/Name) ──────
    [Fact]
    public void Field_moved_to_a_new_path()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[] { KVMigration.Define(2, m => m.RenameSegment("Owner", "Org/Name")) });

        s.Data.Should().NotContainKey("Owner");
        V(s, "Org/Name").Should().Be("Acme");
    }

    // ── Scenario: field-group key renamed (Contact → ContactInfo) ─────────────
    [Fact]
    public void Field_group_key_renamed()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[] { KVMigration.Define(2, m => m.RenameSegment("Contact", "ContactInfo")) });

        s.Data.Should().NotContainKey("Contact/Phone");
        V(s, "ContactInfo/Phone").Should().Be("555-0100");
    }

    // ── Scenario: collection key renamed (Kennels → Cages), carrying $items + items + nested nodes ──
    [Fact]
    public void Collection_key_renamed()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[] { KVMigration.Define(2, m => m.RenameSegment("Kennels", "Cages")) });

        s.Data.Keys.Should().NotContain(key => key.StartsWith("Kennels"));
        s.Data.Should().ContainKey("Cages/$items");
        V(s, "Cages/k1/Occupant/$type").Should().Be(Cat);
        V(s, "Cages/k1/Occupant/Name").Should().Be("Felix");
        V(s, "Cages/k2/Occupant/Breed").Should().Be("lab");
    }

    // ── Scenario: nested-node key renamed under collection wildcards (Occupant → Pet, every item) ──
    [Fact]
    public void Nested_node_key_renamed_for_every_item()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[]
        {
            KVMigration.Define(2, m => m.RenameNode(KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant"), "Pet")),
        });

        s.Data.Keys.Should().NotContain(key => key.Contains("/Occupant"));
        V(s, "Kennels/k1/Pet/$type").Should().Be(Cat);
        V(s, "Kennels/k1/Pet/Name").Should().Be("Felix");
        V(s, "Kennels/k2/Pet/Breed").Should().Be("lab");
    }

    // ── Scenario: structural override / fix (rewrite an existing value across all items) ─────
    [Fact]
    public void Structural_value_override()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[]
        {
            KVMigration.Define(2, m => m.Backfill(
                KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").Seg("Name"),
                view => "Pet:" + view.Absolute(view.Path),
                overwrite: true)),
        });

        V(s, "Kennels/k1/Occupant/Name").Should().Be("Pet:Felix");
        V(s, "Kennels/k2/Occupant/Name").Should().Be("Pet:Rex");
    }

    // ── Scenario: polymorphic field rename — only one subtype (Cat's Whiskers → WhiskerCount) ──
    [Fact]
    public void Polymorphic_field_rename_targets_one_subtype_only()
    {
        var s = ShelterV1();

        KVMigrator.Migrate(s, new[]
        {
            KVMigration.Define(2, m => m.RenameField(
                KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").OfType(Cat).Seg("Whiskers"),
                "WhiskerCount")),
        });

        // Cat item renamed; Dog item never had Whiskers, so nothing leaked onto it.
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Whiskers");
        V(s, "Kennels/k1/Occupant/WhiskerCount").Should().Be(12);
        s.Data.Should().NotContainKey("Kennels/k2/Occupant/WhiskerCount");
    }

    // ── Scenario: replace a nested node TYPE (Cat → Dog) via the DSL, registered on the real model ──
    [Fact]
    public void Replace_nested_node_type_cat_to_dog()
    {
        // The migration DSL for the type swap, declared on the actual model definition:
        var builder = new KVBindBuilder<ShelterRoot>();
        builder.Collection(x => x.Kennels, collection =>
            collection.Item<KennelItem>(item =>
            {
                item.Field(x => x.Code);
                item.NestedNode(x => x.Occupant, nested =>
                {
                    nested.Bind<CatNode>(Cat, cat => { cat.Field(x => x.Name); cat.Field(x => x.Whiskers); });
                    nested.Bind<DogNode>(Dog, dog => { dog.Field(x => x.Name); dog.Field(x => x.Breed); });
                });
            }));

        builder.Migration(2, m => m.ReplaceNestedType(
            KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").OfType(Cat),
            toType: Dog,
            reshape: r => r
                .Drop("Whiskers")               // Cat-only field removed
                .Set("Breed", _ => "unknown")));  // Dog-only field added; Name (common) left untouched

        var definition = builder.Build();
        var s = ShelterV1();

        KVMigrator.Migrate(s, definition);

        // k1 was a Cat → now a Dog: discriminator swapped, common field kept, unique fields reshaped.
        V(s, "Kennels/k1/Occupant/$type").Should().Be(Dog);
        V(s, "Kennels/k1/Occupant/Name").Should().Be("Felix");        // common — untouched
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Whiskers"); // Cat-only — dropped
        V(s, "Kennels/k1/Occupant/Breed").Should().Be("unknown");      // Dog-only — added

        // k2 was already a Dog → completely untouched.
        V(s, "Kennels/k2/Occupant/$type").Should().Be(Dog);
        V(s, "Kennels/k2/Occupant/Breed").Should().Be("lab");
        s.Data.Should().NotContainKey("Kennels/k2/Occupant/Whiskers");
    }

    // ── Scenario: several pending migrations chain, applied in one pass over the real definition ──
    [Fact]
    public void Multiple_registered_migrations_chain_in_order()
    {
        var builder = new KVBindBuilder<ShelterRoot>();
        builder.Collection(x => x.Kennels, collection =>
            collection.Item<KennelItem>(item => item.Field(x => x.Code)));
        builder.Migration(2, m => m.Remove(KVTarget.Root.Seg("LegacyFlag")));
        builder.Migration(3, m => m.RenameNode(KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant"), "Pet"));
        builder.Migration(4, m => m.Backfill(KVTarget.Root.Seg("Kennels").AnyItem().Seg("Pet").Seg("Vaccinated"), _ => true));
        var definition = builder.Build();

        var s = ShelterV1();
        var commits = KVMigrator.Migrate(s, definition);

        commits.Select(c => c.MigrationToVersion).Should().Equal(2, 3, 4);
        s.SchemaVersion.Should().Be(4);
        s.Data.Should().NotContainKey("LegacyFlag");
        // v4 backfilled onto the v3-renamed path — proves the chain sees each prior migration's output.
        V(s, "Kennels/k1/Pet/Vaccinated").Should().Be(true);
        V(s, "Kennels/k1/Pet/Name").Should().Be("Felix");
    }
}

// ── Test model (manual accessors — no source generator) ───────────────────────────────────────────
public sealed class ShelterRoot : KVRootNode
{
    public KVCollectionNode<KennelItem> Kennels { get; } = new();

    public string? Owner
    {
        get => GetField<string?>(nameof(Owner));
        set => SetField(nameof(Owner), value);
    }
}

public sealed class KennelItem : KVCollectionItemNode
{
    public string? Code
    {
        get => GetField<string?>(nameof(Code));
        set => SetField(nameof(Code), value);
    }

    public AnimalNode? Occupant
    {
        get => GetNestedNode<AnimalNode>(nameof(Occupant));
        set => SetNestedNode(nameof(Occupant), value);
    }
}

public abstract class AnimalNode : KVNestedNode
{
    public string? Name
    {
        get => GetField<string?>(nameof(Name));
        set => SetField(nameof(Name), value);
    }
}

public sealed class CatNode : AnimalNode
{
    public int Whiskers
    {
        get => GetField<int>(nameof(Whiskers));
        set => SetField(nameof(Whiskers), value);
    }
}

public sealed class DogNode : AnimalNode
{
    public string? Breed
    {
        get => GetField<string?>(nameof(Breed));
        set => SetField(nameof(Breed), value);
    }
}
