using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

// Pins the KVValue equality/hash contract after replacing JSON-serialize equality with typed structural
// equality. The "unchanged" tests guard behavior that must not regress; the "intended change" tests pin
// the two deliberate improvements (decimal-scale-insensitive, NaN-safe).
public class KVValueEqualityTests
{
    private static KVValue V<T>(T value) => new KVValue<T>(value);

    // ── Scalars: unchanged ────────────────────────────────────────────────────────

    [Fact]
    public void Equal_scalars_of_same_type_are_equal_with_equal_hash()
    {
        var pairs = new (KVValue A, KVValue B)[]
        {
            (V(42), V(42)),
            (V(true), V(true)),
            (V("hello"), V("hello")),
            (V(3.14d), V(3.14d)),
            (V(2.5f), V(2.5f)),
            (V(9.99m), V(9.99m)),
            (V('x'), V('x')),
            (V(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc)), V(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))),
            (V(new DateTimeOffset(new DateTime(2024, 1, 1), TimeSpan.FromHours(2))), V(new DateTimeOffset(new DateTime(2024, 1, 1), TimeSpan.FromHours(2)))),
            (V(TimeSpan.FromMinutes(90)), V(TimeSpan.FromMinutes(90))),
            (V(new TimeOnly(13, 30)), V(new TimeOnly(13, 30))),
            (V(new DateOnly(2024, 6, 1)), V(new DateOnly(2024, 6, 1))),
            (V(Guid.Parse("11111111-1111-1111-1111-111111111111")), V(Guid.Parse("11111111-1111-1111-1111-111111111111"))),
        };

        foreach (var (a, b) in pairs)
        {
            a.Equals(b).Should().BeTrue($"{a.Value} should equal {b.Value}");
            a.GetHashCode().Should().Be(b.GetHashCode());
        }
    }

    [Fact]
    public void Unequal_scalars_of_same_type_are_not_equal()
    {
        V(42).Equals(V(43)).Should().BeFalse();
        V(true).Equals(V(false)).Should().BeFalse();
        V("hello").Equals(V("world")).Should().BeFalse();
        V(3.14d).Equals(V(3.15d)).Should().BeFalse();
        V(Guid.NewGuid()).Equals(V(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public void Different_runtime_types_are_not_equal()
    {
        // Preserves today's same-type rule: a logical "5" stored as int vs decimal is not equal.
        V(5).Equals(V(5m)).Should().BeFalse();
        V(5).Equals(V(5L)).Should().BeFalse();
        V(5).Equals(V("5")).Should().BeFalse();
    }

    [Fact]
    public void Null_values_compare_as_expected()
    {
        V<string?>(null).Equals(V<string?>(null)).Should().BeTrue();
        V<string?>(null).Equals(V("x")).Should().BeFalse();
        V("x").Equals(V<string?>(null)).Should().BeFalse();
        V(0).Equals(null!).Should().BeFalse();
    }

    // ── Arrays / collections: unchanged ───────────────────────────────────────────

    [Fact]
    public void Arrays_compare_by_sequence()
    {
        V(new[] { 1, 2, 3 }).Equals(V(new[] { 1, 2, 3 })).Should().BeTrue();
        V(new[] { 1, 2, 3 }).GetHashCode().Should().Be(V(new[] { 1, 2, 3 }).GetHashCode());

        V(new[] { 1, 2, 3 }).Equals(V(new[] { 1, 2, 4 })).Should().BeFalse();   // different content
        V(new[] { 1, 2, 3 }).Equals(V(new[] { 3, 2, 1 })).Should().BeFalse();   // different order
        V(new[] { 1, 2, 3 }).Equals(V(new[] { 1, 2 })).Should().BeFalse();      // different length

        V(new[] { "a", "b" }).Equals(V(new[] { "a", "b" })).Should().BeTrue();
        V(new[] { "a", "b" }).Equals(V(new[] { "a", "c" })).Should().BeFalse();
    }

    // ── JSON round-trip: the type round-trips and the comparer agrees ──────────────

    [Theory]
    [MemberData(nameof(RoundTripValues))]
    public void Value_equals_itself_after_json_converter_round_trip(KVValue original)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(original, options);
        var reloaded = JsonSerializer.Deserialize<KVValue>(json, options)!;

        reloaded.Value!.GetType().Should().Be(original.Value!.GetType());
        original.Equals(reloaded).Should().BeTrue();
        original.GetHashCode().Should().Be(reloaded.GetHashCode());
    }

    public static IEnumerable<object[]> RoundTripValues() =>
    [
        [V(42)],
        [V("hello")],
        [V(true)],
        [V(9.99m)],
        [V(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc))],
        [V(Guid.Parse("22222222-2222-2222-2222-222222222222"))],
        [V(new[] { 1, 2, 3 })],
        [V(new[] { "a", "b" })],
    ];

    // ── Intended behavior changes ─────────────────────────────────────────────────

    [Fact]
    public void Decimal_equality_is_scale_insensitive()
    {
        // JSON-string equality treated 1.0 != 1.00; typed decimal equality treats them equal.
        V(1.0m).Equals(V(1.00m)).Should().BeTrue();
        V(1.0m).GetHashCode().Should().Be(V(1.00m).GetHashCode());
    }

    [Fact]
    public void NaN_equality_is_true_and_does_not_throw()
    {
        // JSON serialize threw on NaN under Web options; typed double equality is NaN-safe.
        var act = () => V(double.NaN).Equals(V(double.NaN));
        act.Should().NotThrow();
        V(double.NaN).Equals(V(double.NaN)).Should().BeTrue();
        V(float.NaN).Equals(V(float.NaN)).Should().BeTrue();
    }
}
