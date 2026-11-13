using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class ExplicitAllowedValueLookupTests
{
    private static readonly KVExplicitAllowedValue[] Numbers =
    [
        new("one", "One", 1),
        new("two", "Two", 2),
    ];

    private static readonly KVAllowedValuesDefinition NumberElements =
        KVAllowedValuesDefinition.CreateExplicitForElements<int>(Numbers);

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("[2]")]
    [InlineData("[1.0]")]
    public void Elements_WhenJsonArrayHoldsRegisteredNumbers_AreAllowedAndNormalizeToIds(string json)
    {
        using var document = JsonDocument.Parse(json);

        NumberElements.IsAllowed(document.RootElement).Should().BeTrue();
        NumberElements.IsAllowed(json).Should().BeTrue();
    }

    [Fact]
    public void Elements_WhenJsonNumbersAreNormalized_StoresTheirIds()
    {
        using var document = JsonDocument.Parse("[2, 1]");

        NumberElements.NormalizeForStorage(document.RootElement)
            .Should().BeEquivalentTo(new[] { "two", "one" }, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData("[3]")]
    [InlineData("[1.5]")]
    [InlineData("[1e30]")]
    [InlineData("[true]")]
    public void Elements_WhenJsonArrayHoldsAnUnregisteredValue_AreNotAllowed(string json)
    {
        using var document = JsonDocument.Parse(json);

        NumberElements.IsAllowed(document.RootElement).Should().BeFalse();
    }

    [Fact]
    public void Elements_WhenValuesAreEnums_MatchTheirJsonNumbers()
    {
        var days = KVAllowedValuesDefinition.CreateExplicitForElements<DayOfWeek>(
        [
            new("mon", "Monday", DayOfWeek.Monday),
            new("fri", "Friday", DayOfWeek.Friday),
        ]);
        using var document = JsonDocument.Parse("[5, 1]");

        days.IsAllowed(document.RootElement).Should().BeTrue();
        days.NormalizeForStorage(document.RootElement)
            .Should().BeEquivalentTo(new[] { "fri", "mon" }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Scalar_WhenNumberArrivesAsAnotherNumericType_IsAllowedAndNormalizesToItsId()
    {
        var scalar = KVAllowedValuesDefinition.CreateExplicit(Numbers);

        scalar.IsAllowed(2L).Should().BeTrue();
        scalar.NormalizeForStorage(2L).Should().Be("two");
        scalar.IsAllowed(2.5m).Should().BeFalse();
    }

    [Fact]
    public void Elements_WhenStoredIdsAreRead_ReturnTheRegisteredElements()
    {
        NumberElements.DenormalizeFromStorage(new[] { "two", "one" })
            .Should().BeEquivalentTo(new[] { 2, 1 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Elements_WhenAnEntryHasNoValue_OnlySequencesReferencingItFail()
    {
        var definition = KVAllowedValuesDefinition.CreateExplicitForElements<int>(
        [
            new("one", "One", 1),
            new("none", "None", null!),
        ]);

        definition.DenormalizeFromStorage(new[] { "one" }).Should().BeEquivalentTo(new[] { 1 });

        var act = () => definition.DenormalizeFromStorage(new[] { "none" });
        act.Should().Throw<InvalidOperationException>().WithMessage("*not part of allowed values*");
    }
}
