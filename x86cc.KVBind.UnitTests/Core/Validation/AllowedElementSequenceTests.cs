using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class AllowedElementSequenceTests
{
    // Elements are words, tokens are their lengths: "one" <-> 3, "three" <-> 5.
    private static readonly KVAllowedValuesDefinition Words =
        KVAllowedValuesDefinition.CreateForElements(new[] { "one", "three" }, word => word.Length);

    [Fact]
    public void Denormalize_WhenValueIsNotASequence_Throws()
    {
        var act = () => Words.DenormalizeFromStorage(42);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not part of allowed values*");
    }

    [Fact]
    public void Denormalize_WhenItemsAreTokens_ReturnsElements()
    {
        var result = Words.DenormalizeFromStorage(new object[] { 3, 5 });

        result.Should().BeEquivalentTo(new[] { "one", "three" }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Denormalize_WhenItemsAreAlreadyElements_KeepsThem()
    {
        var result = Words.DenormalizeFromStorage(new object[] { "three", "one" });

        result.Should().BeEquivalentTo(new[] { "three", "one" }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Denormalize_WhenItemIsNeitherTokenNorElement_Throws()
    {
        var act = () => Words.DenormalizeFromStorage(new object[] { 3, 2.5 });

        act.Should().Throw<InvalidOperationException>().WithMessage("*not part of allowed values*");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("3")]
    [InlineData("\"one\"")]
    [InlineData("[true]")]
    [InlineData("[false]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[2.5]")]
    public void IsAllowed_WhenJsonIsNotAnArrayOfTokens_ReturnsFalse(string json)
    {
        using var document = JsonDocument.Parse(json);

        Words.IsAllowed(document.RootElement).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_WhenJsonArrayHoldsNumberTokens_ReturnsTrue()
    {
        using var document = JsonDocument.Parse("[3, 5]");

        Words.IsAllowed(document.RootElement).Should().BeTrue();
    }
}
