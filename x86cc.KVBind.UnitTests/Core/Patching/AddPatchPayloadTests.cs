using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class AddPatchPayloadTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_WhenOnlyItemIdIsSent_BuildsPayload()
    {
        var payload = JsonSerializer.Deserialize<KVAddPatchPayload>("""{"itemId":"item-1"}""", WebOptions);

        payload.Should().Be(new KVAddPatchPayload("item-1"));
    }

    [Fact]
    public void Deserialize_WhenTypeTokenIsSent_KeepsIt()
    {
        var payload = JsonSerializer.Deserialize<KVAddPatchPayload>("""{"itemId":"item-1","typeToken":"special"}""", WebOptions);

        payload!.TypeToken.Should().Be("special");
    }

    [Fact]
    public void Serialize_RoundTripsThroughJson()
    {
        var original = new KVAddPatchPayload("item-1", "special");

        var json = JsonSerializer.Serialize(original, WebOptions);

        JsonSerializer.Deserialize<KVAddPatchPayload>(json, WebOptions).Should().Be(original);
    }

    [Fact]
    public void GuidConstructor_UsesTheDFormat()
    {
        var id = Guid.NewGuid();

        new KVAddPatchPayload(id).ItemId.Should().Be(id.ToString("D"));
        new KVAddPatchPayload(Guid.Empty).ItemId.Should().BeEmpty();
    }

    [Fact]
    public void Deconstruct_AndWith_KeepWorking()
    {
        var (itemId, typeToken) = new KVAddPatchPayload("item-1", "special") with { TypeToken = "other" };

        itemId.Should().Be("item-1");
        typeToken.Should().Be("other");
    }
}
