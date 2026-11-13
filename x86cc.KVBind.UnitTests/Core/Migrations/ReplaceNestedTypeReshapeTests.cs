using AwesomeAssertions;
using x86cc.KVBind.Core.Migrations;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core.Migrations;

public class ReplaceNestedTypeReshapeTests
{
    private const string Cat = "CAT";
    private const string Dog = "DOG";

    private static readonly KVTarget CatOccupant = KVTarget.Root.Seg("Kennels").AnyItem().Seg("Occupant").OfType(Cat);

    [Fact]
    public void Rename_carries_the_value_to_the_new_field_and_removes_the_old_one()
    {
        var s = Shelter();

        Reshape(s, r => r.Rename("Whiskers", "Fur"));

        V(s, "Kennels/k1/Occupant/$type").Should().Be(Dog);
        V(s, "Kennels/k1/Occupant/Fur").Should().Be(12);
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Whiskers");
    }

    [Fact]
    public void Rename_of_an_absent_field_changes_nothing_but_the_type()
    {
        var s = Shelter();

        Reshape(s, r => r.Rename("Missing", "Fur"));

        V(s, "Kennels/k1/Occupant/$type").Should().Be(Dog);
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Fur");
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Missing");
    }

    [Fact]
    public void Rename_of_a_subtree_moves_every_leaf_beneath_it()
    {
        var s = Shelter();

        Reshape(s, r => r.Rename("Tags", "Labels"));

        V(s, "Kennels/k1/Occupant/Labels/First").Should().Be("indoor");
        V(s, "Kennels/k1/Occupant/Labels/Second").Should().Be("shy");
        s.Data.Keys.Should().NotContain(key => key.StartsWith("Kennels/k1/Occupant/Tags", StringComparison.Ordinal));
    }

    [Fact]
    public void Rename_does_not_touch_a_field_that_merely_shares_the_prefix()
    {
        var s = Shelter();
        s.Data["Kennels/k1/Occupant/TagsCount"] = KVValue.FromObject(2);

        Reshape(s, r => r.Rename("Tags", "Labels"));

        V(s, "Kennels/k1/Occupant/TagsCount").Should().Be(2);
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/LabelsCount");
    }

    [Fact]
    public void Rename_onto_a_field_that_already_holds_data_throws()
    {
        var s = Shelter();

        var act = () => Reshape(s, r => r.Rename("Whiskers", "Name"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*destination already holds data*");
        V(s, "Kennels/k1/Occupant/$type").Should().Be(Cat);
        V(s, "Kennels/k1/Occupant/Name").Should().Be("Felix");
    }

    [Fact]
    public void Rename_onto_a_dropped_destination_succeeds()
    {
        var s = Shelter();

        Reshape(s, r => r
            .Drop("Name")
            .Rename("Whiskers", "Name"));

        V(s, "Kennels/k1/Occupant/Name").Should().Be(12);
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Whiskers");
    }

    [Fact]
    public void Rename_to_the_same_name_changes_nothing()
    {
        var s = Shelter();

        Reshape(s, r => r.Rename("Name", "Name"));

        V(s, "Kennels/k1/Occupant/Name").Should().Be("Felix");
    }

    [Fact]
    public void Drop_of_a_subtree_removes_every_leaf_beneath_it()
    {
        var s = Shelter();

        Reshape(s, r => r.Drop("Tags"));

        s.Data.Keys.Should().NotContain(key => key.StartsWith("Kennels/k1/Occupant/Tags", StringComparison.Ordinal));
        V(s, "Kennels/k1/Occupant/Name").Should().Be("Felix");
    }

    [Fact]
    public void Drop_of_an_absent_path_writes_nothing()
    {
        var s = Shelter();
        var keysBefore = s.Data.Keys.ToArray();

        Reshape(s, r => r.Drop("Missing"));

        s.Data.Keys.Should().BeEquivalentTo(keysBefore);
    }

    [Fact]
    public void Set_without_overwrite_keeps_an_existing_value_and_fills_an_absent_one()
    {
        var s = Shelter();

        Reshape(s, r => r
            .Set("Name", _ => "Renamed", overwrite: false)
            .Set("Breed", _ => "unknown", overwrite: false));

        V(s, "Kennels/k1/Occupant/Name").Should().Be("Felix");
        V(s, "Kennels/k1/Occupant/Breed").Should().Be("unknown");
    }

    [Fact]
    public void Set_with_overwrite_replaces_the_value_and_null_removes_it()
    {
        var s = Shelter();

        Reshape(s, r => r
            .Set("Name", _ => "Rex")
            .Set("Whiskers", _ => null));

        V(s, "Kennels/k1/Occupant/Name").Should().Be("Rex");
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Whiskers");
    }

    [Fact]
    public void Set_with_overwrite_to_the_identical_value_leaves_it_in_place()
    {
        var s = Shelter();

        Reshape(s, r => r.Set("Name", _ => "Felix"));

        V(s, "Kennels/k1/Occupant/Name").Should().Be("Felix");
        V(s, "Kennels/k1/Occupant/$type").Should().Be(Dog);
    }

    [Fact]
    public void Set_without_overwrite_after_a_rename_keeps_the_renamed_value()
    {
        var s = Shelter();

        Reshape(s, r => r
            .Rename("Whiskers", "Fur")
            .Set("Fur", _ => 0, overwrite: false));

        V(s, "Kennels/k1/Occupant/Fur").Should().Be(12);
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Whiskers");
    }

    [Fact]
    public void Rename_after_a_set_carries_the_newly_set_value()
    {
        var s = Shelter();

        Reshape(s, r => r
            .Set("Name", _ => "Rex")
            .Rename("Name", "Alias"));

        V(s, "Kennels/k1/Occupant/Alias").Should().Be("Rex");
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Name");
    }

    [Fact]
    public void Set_after_a_drop_of_its_parent_recreates_only_that_leaf()
    {
        var s = Shelter();

        Reshape(s, r => r
            .Drop("Tags")
            .Set("Tags/First", _ => "outdoor", overwrite: false));

        V(s, "Kennels/k1/Occupant/Tags/First").Should().Be("outdoor");
        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Tags/Second");
    }

    [Fact]
    public void Drop_after_a_set_removes_the_newly_set_value()
    {
        var s = Shelter();

        Reshape(s, r => r
            .Set("Breed", _ => "unknown")
            .Drop("Breed"));

        s.Data.Should().NotContainKey("Kennels/k1/Occupant/Breed");
        V(s, "Kennels/k1/Occupant/$type").Should().Be(Dog);
    }

    private static void Reshape(KVSnapshot snapshot, Action<KVNodeReshapeBuilder> reshape)
        => KVMigrator.Migrate(snapshot, new[] { KVMigration.Define(2, m => m.ReplaceNestedType(CatOccupant, Dog, reshape)) });

    private static KVSnapshot Shelter()
    {
        var snapshot = new KVSnapshot { SchemaVersion = 1, LastCommitId = Guid.NewGuid() };
        snapshot.Data["Kennels/$items"] = KVValue.FromObject(new[] { "k1" });
        snapshot.Data["Kennels/k1/Occupant/$type"] = Cat;
        snapshot.Data["Kennels/k1/Occupant/Name"] = "Felix";
        snapshot.Data["Kennels/k1/Occupant/Whiskers"] = KVValue.FromObject(12);
        snapshot.Data["Kennels/k1/Occupant/Tags/First"] = "indoor";
        snapshot.Data["Kennels/k1/Occupant/Tags/Second"] = "shy";
        return snapshot;
    }

    private static object? V(KVSnapshot s, string path) => s.Data.TryGetValue(path, out var v) ? v?.Value : null;
}
