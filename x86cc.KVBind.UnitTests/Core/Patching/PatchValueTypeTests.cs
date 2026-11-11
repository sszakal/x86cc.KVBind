using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class PatchValueTypeTests : KVModelTestBase
{
    public PatchValueTypeTests()
    {
        this.RegisterModelDefinition<PvtRoot>(builder =>
        {
            builder.Field(x => x.Name);
            builder.Field(x => x.Amount);
            builder.Field(x => x.Count);
            builder.Field(x => x.Rate);
            builder.Field(x => x.Active);
            builder.Field(x => x.Signed);
            builder.Field(x => x.Reference);
            builder.Field(x => x.Kind);
            builder.Field(x => x.Codes);
            builder.Field(x => x.Status, f => f.AllowedValues("open", "closed"));
        });
    }

    [Fact]
    public void Definition_ExposesTheDeclaredFieldType()
    {
        var definition = this.CreateRegistry().Get<PvtRoot>();

        definition.FindField("Amount")!.ValueType.Should().Be(typeof(decimal));
        definition.FindField("Rate")!.ValueType.Should().Be(typeof(decimal?));
        definition.FindField("Name")!.ValueType.Should().Be(typeof(string));
    }

    [Fact]
    public void Set_KeepsAValueThatAlreadyHasTheFieldType()
    {
        var root = this.CreateRoot<PvtRoot>();

        root.Patch(
            KVPatchOperation.Set("/Name", "Contract"),
            KVPatchOperation.Set("/Amount", 12.5m),
            KVPatchOperation.Set("/Active", true));

        root.Name.Should().Be("Contract");
        root.Amount.Should().Be(12.5m);
        root.Active.Should().BeTrue();
    }

    [Fact]
    public void Set_ConvertsLosslessNumericValues()
    {
        var root = this.CreateRoot<PvtRoot>();

        root.Patch(
            KVPatchOperation.Set("/Amount", 42),
            KVPatchOperation.Set("/Count", 7m),
            KVPatchOperation.Set("/Rate", 0.25));

        root.Amount.Should().Be(42m);
        root.Count.Should().Be(7);
        root.Rate.Should().Be(0.25m);
    }

    [Fact]
    public void Set_ConvertsStringsToNonStringFields()
    {
        var root = this.CreateRoot<PvtRoot>();
        var reference = Guid.Parse("11111111-2222-3333-4444-555555555555");

        root.Patch(
            KVPatchOperation.Set("/Amount", "12.50"),
            KVPatchOperation.Set("/Signed", "2026-03-01T00:00:00+00:00"),
            KVPatchOperation.Set("/Reference", reference.ToString()),
            KVPatchOperation.Set("/Kind", "Exclusive"),
            KVPatchOperation.Set("/Codes", "[1,2,3]"));

        root.Amount.Should().Be(12.50m);
        root.Signed.Should().Be(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        root.Reference.Should().Be(reference);
        root.Kind.Should().Be(PvtKind.Exclusive);
        root.Codes.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Set_ConvertsJsonElements()
    {
        var root = this.CreateRoot<PvtRoot>();
        var json = JsonDocument.Parse("""{ "amount": 99.9, "codes": [4, 5], "name": "From JSON", "active": true }""").RootElement;

        root.Patch(
            KVPatchOperation.Set("/Amount", json.GetProperty("amount")),
            KVPatchOperation.Set("/Codes", json.GetProperty("codes")),
            KVPatchOperation.Set("/Name", json.GetProperty("name")),
            KVPatchOperation.Set("/Active", json.GetProperty("active")));

        root.Amount.Should().Be(99.9m);
        root.Codes.Should().Equal(4, 5);
        root.Name.Should().Be("From JSON");
        root.Active.Should().BeTrue();
    }

    [Theory]
    [InlineData("/Amount", "abc", "System.Decimal")]
    [InlineData("/Count", 1.5, "System.Int32")]
    [InlineData("/Count", 5000000000L, "System.Int32")]
    [InlineData("/Name", 5, "System.String")]
    [InlineData("/Active", 1, "System.Boolean")]
    [InlineData("/Rate", "n/a", "System.Decimal")]
    [InlineData("/Reference", "not-a-guid", "System.Guid")]
    [InlineData("/Kind", "Unknown", "x86cc.KVBind.UnitTests.Core.PvtKind")]
    [InlineData("/Codes", "1,2", "System.Int32[]")]
    public void Set_RejectsAValueThatDoesNotFitTheField_AndLeavesItUntouched(string path, object value, string expectedType)
    {
        var root = this.CreateRoot<PvtRoot>();

        var act = () => root.Patch(KVPatchOperation.Set(path, value));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*for field '{path.TrimStart('/')}' cannot be converted to '{expectedType}'.");
        root.GetAllChanges().Changes.Should().BeEmpty();
    }

    [Fact]
    public void Set_Null_ClearsTheFieldWhateverItsType()
    {
        var root = this.CreateRoot<PvtRoot>();
        root.Amount = 5m;
        root.Rate = 1m;

        root.Patch(KVPatchOperation.Set("/Amount", null), KVPatchOperation.Set("/Rate", null));

        root.Amount.Should().Be(0m);
        root.Rate.Should().BeNull();
    }

    [Fact]
    public void Set_OnAnAllowedValuesField_StillKeepsUnknownTokensForValidation()
    {
        var root = this.CreateRoot<PvtRoot>();

        root.Patch(KVPatchOperation.Set("/Status", "bogus"));

        root.Validate().Errors.Should().ContainSingle(e => e.Path == "Status" && e.Code == "allowed_values");
    }
}

public enum PvtKind
{
    Standard,
    Exclusive
}

public partial class PvtRoot : KVRootNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }

    [KVBind("Amount")]
    public partial decimal Amount { get; set; }

    [KVBind("Count")]
    public partial int Count { get; set; }

    [KVBind("Rate")]
    public partial decimal? Rate { get; set; }

    [KVBind("Active")]
    public partial bool Active { get; set; }

    [KVBind("Signed")]
    public partial DateTimeOffset? Signed { get; set; }

    [KVBind("Reference")]
    public partial Guid? Reference { get; set; }

    [KVBind("Kind")]
    public partial PvtKind Kind { get; set; }

    [KVBind("Codes")]
    public partial int[]? Codes { get; set; }

    [KVBind("Status")]
    public partial string? Status { get; set; }
}
