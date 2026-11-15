using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class KVValueJsonConverterTests
{
    [Flags]
    public enum Access
    {
        None = 0,
        Read = 1,
        Write = 2,
    }

    public enum Color
    {
        Red,
        Green,
    }

    [Fact]
    public void AnUnknownValueType_FailsEveryTime_AndIsNotCached()
    {
        var typeName = $"Nowhere.Type{Guid.NewGuid():N}, Nowhere.Assembly";
        var json = $$"""{"$type":"{{typeName}}","value":1}""";

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var act = () => JsonSerializer.Deserialize<KVValue>(json);
            act.Should().Throw<JsonException>().WithMessage($"*'{typeName}'*");
        }

        KVValueJsonConverter.IsCached(typeName).Should().BeFalse("a type that loads later must still resolve then");
    }

    [Fact]
    public void AResolvedValueType_IsCached()
    {
        var json = JsonSerializer.Serialize<KVValue>(new KVValue<Color>(Color.Red));

        JsonSerializer.Deserialize<KVValue>(json)!.Value.Should().Be(Color.Red);

        using var document = JsonDocument.Parse(json);
        KVValueJsonConverter.IsCached(document.RootElement.GetProperty("$type").GetString()!).Should().BeTrue();
    }

    [Fact]
    public void EnumsWrittenAsNames_ByAHostThatSerializesEnumsAsStrings_ReadBack()
    {
        // e.g. Marten with EnumStorage.AsString: the converter writes with the host's options.
        var hostOptions = new JsonSerializerOptions { Converters = { new KVValueJsonConverter(), new JsonStringEnumConverter() } };

        var json = JsonSerializer.Serialize<KVValue>(new KVValue<Color>(Color.Green), hostOptions);
        var flags = JsonSerializer.Serialize<KVValue>(new KVValue<Access>(Access.Read | Access.Write), hostOptions);

        json.Should().Contain("\"Green\"");
        JsonSerializer.Deserialize<KVValue>(json, hostOptions)!.Value.Should().Be(Color.Green);
        JsonSerializer.Deserialize<KVValue>(flags, hostOptions)!.Value.Should().Be(Access.Read | Access.Write);
    }

    [Fact]
    public void EnumsWrittenAsNumbers_StillReadBack()
    {
        var hostOptions = new JsonSerializerOptions { Converters = { new KVValueJsonConverter() } };

        var json = JsonSerializer.Serialize<KVValue>(new KVValue<Color>(Color.Green), hostOptions);

        JsonSerializer.Deserialize<KVValue>(json, hostOptions)!.Value.Should().Be(Color.Green);
    }
}
