using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core.Patching;

public enum AvPriority
{
    Low = 1,
    High = 2,
    Urgent = 10,
}

public enum AvLevel
{
    Low,
    High,
}

// SET on fields with allowed values, with the forms a client actually sends: a JSON request carries tokens
// and typed values alike as strings and numbers, so the field's definition decides what a value means.
public class AllowedValuePatchTests : KVModelTestBase
{
    // Tokens are the wire form: 10 means Low here, even though Urgent's numeric value is also 10.
    [Theory]
    [InlineData(10, AvPriority.Low)]
    [InlineData(20, AvPriority.High)]
    public void Set_TokenOfATokenMappedEnum_StoresItsValue(int token, AvPriority expected)
    {
        var root = this.NewRoot();

        root.Patch(KVPatchOperation.Set("/Priority", token));
        root.Priority.Should().Be(expected);

        root.Patch(KVPatchOperation.Set("/Priority", Json(token.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        root.Priority.Should().Be(expected);
    }

    // Tokens are the wire form even when a token equals another allowed value of the field's own type:
    // values 1 and 10 with tokens 10 and 100, so 10 is the token of the value 1.
    [Theory]
    [InlineData("10", 1)]
    [InlineData("100", 10)]
    public void Set_ATokenThatIsAlsoAnotherAllowedValue_IsReadAsTheToken(string json, int expected)
    {
        var root = this.NewRoot();

        root.Patch(KVPatchOperation.Set("/Rate", Json(json)));

        root.Rate.Should().Be(expected);
    }

    [Fact]
    public void Set_AFractionalNumberForAnAllowedInt_IsRejected()
    {
        var root = this.NewRoot();

        var act = () => root.Patch(KVPatchOperation.Set("/Count", Json("2.5")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*allowed values*");
    }

    [Fact]
    public void Set_TypedValueOfATokenMappedEnum_StillWorks()
    {
        var root = this.NewRoot();

        root.Patch(KVPatchOperation.Set("/Priority", AvPriority.High));

        root.Priority.Should().Be(AvPriority.High);
    }

    [Fact]
    public void Set_ATokenNotDeclared_IsRejected_EvenWhenItIsAnEnumMembersNumber()
    {
        var root = this.NewRoot();

        var act = () => root.Patch(KVPatchOperation.Set("/Priority", 30));

        act.Should().Throw<InvalidOperationException>().WithMessage("*allowed values*");
    }

    // The schema publishes the options of a plain value list as text ("1", "2", "3"), so a UI sends them back
    // as JSON strings; they must be stored as the field's type, readable by the typed property.
    [Theory]
    [InlineData("\"2\"")]
    [InlineData("2")]
    [InlineData("2.0")]
    [InlineData("2.00")]
    public void Set_AnAllowedIntAsJsonTextOrNumber_StoresTheInt(string json)
    {
        var root = this.NewRoot();

        root.Patch(KVPatchOperation.Set("/Count", Json(json)));

        root.Count.Should().Be(2);
    }

    [Fact]
    public void Set_AnAllowedEnumByName_StoresTheMember()
    {
        var root = this.NewRoot();

        root.Patch(KVPatchOperation.Set("/Level", Json("\"High\"")));

        root.Level.Should().Be(AvLevel.High);
    }

    [Theory]
    [InlineData("\"7\"")]
    [InlineData("\"many\"")]
    public void Set_TextThatIsNoAllowedValueOfANonTextField_IsRejected(string json)
    {
        var root = this.NewRoot();

        var act = () => root.Patch(KVPatchOperation.Set("/Count", Json(json)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*allowed values*");
        root.Count.Should().BeNull();
    }

    [Fact]
    public void Set_AnUnknownTextToken_IsKeptForValidation()
    {
        var root = this.NewRoot();

        root.Patch(KVPatchOperation.Set("/Currency", Json("\"usd\"")));

        root.Validate().Errors.Should().ContainSingle(e => e.Path == "Currency" && e.Code == "allowed_values");
    }

    [Theory]
    [InlineData("[\"eur\"]")]
    [InlineData("{\"id\":\"eur\"}")]
    public void Set_AnArrayOrObjectOnASingleValueField_IsRejected(string json)
    {
        var root = this.NewRoot();

        var act = () => root.Patch(KVPatchOperation.Set("/Currency", Json(json)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*single value*");
    }

    [Fact]
    public void Set_AJsonArrayOnAnElementWiseField_StillMatchesEachElement()
    {
        var root = this.NewRoot();

        root.Patch(KVPatchOperation.Set("/Tags", Json("[\"red\",\"blue\"]")));

        root.Tags.Should().Equal("red", "blue");
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private AvRoot NewRoot()
    {
        this.RegisterModelDefinition<AvRoot>(builder =>
        {
            builder.Field(x => x.Priority, f => f.AllowedValues([AvPriority.Low, AvPriority.High], value => value == AvPriority.Low ? 10 : 20));
            builder.Field(x => x.Count, f => f.AllowedValues(1, 2, 3));
            builder.Field(x => x.Rate, f => f.AllowedValues([1m, 10m], value => value == 1m ? 10 : 100));
            builder.Field(x => x.Level, f => f.AllowedValues(AvLevel.Low, AvLevel.High));
            builder.Field(x => x.Currency, f => f.AllowedValue("EUR", "eur", "Euro").AllowedValue("CHF", "chf", "Swiss Franc"));
            builder.Field(x => x.Tags, f => f.AllowedElementValue<string>("red", "red", "Red").AllowedElementValue<string>("blue", "blue", "Blue"));
        });
        return this.CreateRoot<AvRoot>(model: null);
    }
}

public partial class AvRoot : KVRootNode
{
    [KVBind("Priority")]
    public partial AvPriority? Priority { get; set; }

    [KVBind("Count")]
    public partial int? Count { get; set; }

    [KVBind("Rate")]
    public partial decimal? Rate { get; set; }

    [KVBind("Level")]
    public partial AvLevel? Level { get; set; }

    [KVBind("Currency")]
    public partial string? Currency { get; set; }

    [KVBind("Tags")]
    public partial string[]? Tags { get; set; }
}
