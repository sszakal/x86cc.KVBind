using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

// SET values as they arrive from a web client: JsonElements, including null and allowed-value tokens.
public class JsonPatchValueTests : KVModelTestBase
{
    public JsonPatchValueTests()
    {
        this.RegisterModelDefinition<JpvRoot>(builder =>
        {
            builder.Field(x => x.Amount);
            builder.Field(x => x.Name);
            builder.Field(x => x.Level, f => f.AllowedValues(1, 2, 3));
            builder.Field(x => x.Tier, f => f.AllowedValue("gold", "gold", "Gold").AllowedValue("silver", "silver", "Silver"));
            builder.Field(x => x.Tags, f => f
                .AllowedElementValue<string>("a", "a", "A")
                .AllowedElementValue<string>("b", "b", "B"));
        });
    }

    [Fact]
    public void Set_WithAJsonNull_ClearsTheField()
    {
        var root = this.CreateRoot<JpvRoot>();
        root.Patch(KVPatchOperation.Set("/Amount", 5m), KVPatchOperation.Set("/Name", "x"));

        root.Patch(KVPatchOperation.Set("/Amount", Json("null")), KVPatchOperation.Set("/Name", Json("null")));

        root.Amount.Should().BeNull();
        root.Name.Should().BeNull();
    }

    [Fact]
    public void Set_WithAJsonNumber_MatchesAnAllowedValueOfTheFieldType()
    {
        var root = this.CreateRoot<JpvRoot>();

        root.Patch(KVPatchOperation.Set("/Level", Json("2")));

        root.Level.Should().Be(2);
    }

    [Fact]
    public void Set_WithAJsonNumberNotAllowed_IsRejected()
    {
        var root = this.CreateRoot<JpvRoot>();

        var act = () => root.Patch(KVPatchOperation.Set("/Level", Json("9")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*allowed values*");
    }

    [Fact]
    public void Set_WithAJsonString_MatchesAnAllowedToken_AndKeepsAnUnknownOneForValidation()
    {
        var root = this.CreateRoot<JpvRoot>();

        root.Patch(KVPatchOperation.Set("/Tier", Json("\"gold\"")));
        root.Tier.Should().Be("gold");

        root.Patch(KVPatchOperation.Set("/Tier", Json("\"platinum\"")));
        root.Validate().Errors.Should().ContainSingle().Which.Code.Should().Be("allowed_values");
    }

    [Fact]
    public void Set_WithAJsonArray_StoresTheAllowedElementTokens()
    {
        var root = this.CreateRoot<JpvRoot>();

        root.Patch(KVPatchOperation.Set("/Tags", Json("[\"b\",\"a\"]")));

        root.Tags.Should().Equal("b", "a");
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

public partial class JpvRoot : KVRootNode
{
    [KVBind(nameof(Amount))]
    public partial decimal? Amount { get; set; }

    [KVBind(nameof(Name))]
    public partial string? Name { get; set; }

    [KVBind(nameof(Level))]
    public partial int? Level { get; set; }

    [KVBind(nameof(Tier))]
    public partial string? Tier { get; set; }

    [KVBind(nameof(Tags))]
    public partial string[]? Tags { get; set; }
}
