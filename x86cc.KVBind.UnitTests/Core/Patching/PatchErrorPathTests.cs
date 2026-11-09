using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class PatchErrorPathTests : KVModelTestBase
{
    public PatchErrorPathTests()
    {
        this.RegisterModelDefinition<PatchErrorModel>(builder =>
        {
            builder.Field(x => x.Title);
            builder.Field(x => x.Choice, field => field.AllowedValues(FieldEnum.First, FieldEnum.Second));
            builder.Collection(x => x.Items, collection =>
            {
                collection.Operation<ItemsToGroup>("GROUP", x => x.GroupItems);
                collection.Operation<ItemsToGroup>("NOTE", x => x.Note);
                collection.Operation<int>("BUMP", x => x.Bump);
                collection.Item<ChangeSetPatchItemNode>(item => item.Field(x => x.Name));
            });
        });
    }

    // ── Payload validation when an operation is constructed ──────────────────

    public static TheoryData<string, object?, string> InvalidPayloads => new()
    {
        { "UNSET", "x", "Patch operation 'UNSET' does not accept values." },
        { "discard", "x", "Patch operation 'discard' does not accept values." },
        { "REMOVE", 1, "Patch operation 'REMOVE' does not accept values." },
        { "DROP", true, "Patch operation 'DROP' does not accept values." },
        { "ADD", null, "ADD patch operations require a KVAddPatchPayload value." },
        { "add", Guid.NewGuid(), "ADD patch operations require a KVAddPatchPayload value." },
        { "ADD", new KVAddPatchPayload(Guid.Empty), "ADD patch operations require a non-empty item id." },
        { "MOVE", "0", "MOVE patch operations require an integer target index or KVMovePatchPayload value." },
        { "MOVE", null, "MOVE patch operations require an integer target index or KVMovePatchPayload value." },
        { "INIT", null, "INIT patch operations require a non-empty nested-node type token value." },
        { "INIT", " ", "INIT patch operations require a non-empty nested-node type token value." },
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void PatchOperation_WhenPayloadDoesNotMatchBuiltInOperation_ThrowsOnConstruction(string operation, object? value, string message)
    {
        var act = () => KVPatchOperation.Custom(operation, "/Title", value);

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    // ── Operation sent to the wrong kind of target ──────────────────────────

    [Theory]
    [InlineData("SET", "/Items", "x")]
    [InlineData("UNSET", "/Items", null)]
    [InlineData("REMOVE", "/Title", null)]
    [InlineData("INIT", "/Title", "DOG")]
    [InlineData("GROUP", "/Title", null)]
    public void PatchExecution_WhenOperationTargetsWrongKindOfMember_Throws(string operation, string path, object? value)
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();

        var act = () => model.Patch(KVPatchOperation.Custom(operation, path, value));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Patch operation '{operation}' is not valid for path '{path}' resolved to '{path.TrimStart('/')}'.");
    }

    [Fact]
    public void PatchExecution_WhenItemLevelOperationTargetsItem_ButOperationIsCollectionLevel_Throws()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        var itemId = model.Items.GetItemId(model.Items.Create());
        model.CommitSetup();

        var act = () => model.Patch(KVPatchOperation.Add($"/Items/{itemId}", new KVAddPatchPayload(Guid.NewGuid())));

        act.Should().Throw<InvalidOperationException>().WithMessage("Patch operation 'ADD' is not valid for path*");
    }

    // ── SET against allowed values ───────────────────────────────────────────

    [Fact]
    public void PatchExecution_WhenSetValueIsOutsideAllowedValues_Throws()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();

        var act = () => model.Patch(KVPatchOperation.Set("/Choice", FieldEnum.Third));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Value for field 'Choice' is not part of configured allowed values.");
    }

    [Fact]
    public void PatchExecution_WhenSetValueIsAnAllowedValue_Applies()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();

        model.Patch(KVPatchOperation.Set("/Choice", FieldEnum.Second));

        model.Choice.Should().Be(FieldEnum.Second);
    }

    // ── MOVE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PatchExecution_WhenMoveUsesPayload_ReordersItems()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        var firstId = model.Items.GetItemId(model.Items.Create());
        var secondId = model.Items.GetItemId(model.Items.Create());
        model.CommitSetup();

        model.Patch(KVPatchOperation.Move($"/Items/{secondId}", new KVMovePatchPayload(0)));

        model.Items.GetActiveItemIds().Should().Equal(secondId, firstId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void PatchExecution_WhenMoveIndexIsOutOfRange_Throws(int toIndex)
    {
        var model = this.CreateRoot<PatchErrorModel>();
        var firstId = model.Items.GetItemId(model.Items.Create());
        _ = model.Items.Create();
        model.CommitSetup();

        var act = () => model.Patch(KVPatchOperation.Move($"/Items/{firstId}", toIndex));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PatchExecution_WhenMoveTargetsMissingItem_Throws()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();
        var missingId = Guid.NewGuid().ToString("D");

        var act = () => model.Patch(KVPatchOperation.Move($"/Items/{missingId}", 0));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Collection child '{missingId}' not found for path '/Items/{missingId}'.");
    }

    // ── Custom operation arguments ───────────────────────────────────────────

    [Fact]
    public void CustomOperation_WhenArgumentIsJsonElement_DeserializesIt()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();
        var itemId = Guid.NewGuid();
        using var document = JsonDocument.Parse($$"""{"ItemId":"{{itemId}}"}""");

        model.Patch(KVPatchOperation.Custom("GROUP", "/Items", document.RootElement.Clone()));

        model.Items.GetById(itemId.ToString("D")).Should().NotBeNull();
    }

    [Fact]
    public void CustomOperation_WhenJsonArgumentUsesCamelCase_BindsProperties()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();
        var itemId = Guid.NewGuid();
        using var document = JsonDocument.Parse($$"""{"itemId":"{{itemId}}"}""");

        model.Patch(KVPatchOperation.Custom("NOTE", "/Items", document.RootElement.Clone()));

        model.Notes.Should().ContainSingle().Which!.ItemId.Should().Be(itemId);
    }

    [Fact]
    public void CustomOperation_WhenNumericArgumentIsSentAsJsonString_ConvertsIt()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();
        using var document = JsonDocument.Parse("\"12\"");

        model.Patch(KVPatchOperation.Custom("BUMP", "/Items", document.RootElement.Clone()));

        model.Bumps.Should().Equal(12);
    }

    [Fact]
    public void CustomOperation_WhenArgumentHasMatchingShape_ConvertsIt()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();
        var itemId = Guid.NewGuid();

        model.Patch(KVPatchOperation.Custom("GROUP", "/Items", new { ItemId = itemId }));

        model.Items.GetById(itemId.ToString("D")).Should().NotBeNull();
    }

    [Fact]
    public void CustomOperation_WhenNumericArgumentHasDifferentClrType_ConvertsIt()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();
        using var document = JsonDocument.Parse("7");

        model.Patch(KVPatchOperation.Custom("BUMP", "/Items", 5L));
        model.Patch(KVPatchOperation.Custom("BUMP", "/Items", document.RootElement.Clone()));

        model.Bumps.Should().Equal(5, 7);
    }

    [Fact]
    public void CustomOperation_WhenReferenceArgumentIsNull_PassesNull()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();

        model.Patch(KVPatchOperation.Custom("NOTE", "/Items"));

        model.Notes.Should().Equal([null]);
    }

    [Fact]
    public void CustomOperation_WhenValueTypeArgumentIsNull_Throws()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();

        var act = () => model.Patch(KVPatchOperation.Custom("BUMP", "/Items"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Patch operation 'BUMP' for '/Items' requires a 'System.Int32' payload.");
        model.Bumps.Should().BeEmpty();
    }

    [Fact]
    public void CustomOperation_WhenArgumentCannotBeConverted_ThrowsWithInnerJsonException()
    {
        var model = this.CreateRoot<PatchErrorModel>();
        model.CommitSetup();

        var act = () => model.Patch(KVPatchOperation.Custom("BUMP", "/Items", "not a number"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Patch operation 'BUMP' for '/Items' payload cannot be converted to 'System.Int32'.")
            .WithInnerException<JsonException>();
        model.Bumps.Should().BeEmpty();
    }

    // ── Custom operation registration ────────────────────────────────────────

    [Fact]
    public void CustomOperation_WhenNameIsRegisteredTwiceIgnoringCase_Throws()
    {
        var act = () => this.BuildWithOperations(collection =>
        {
            collection.Operation<ItemsToGroup>("GROUP", x => x.GroupItems);
            collection.Operation<ItemsToGroup>("group", x => x.Note);
        });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Patch operation 'GROUP' is already registered for this collection.");
    }

    [Fact]
    public void CustomOperation_WhenSelectorIsNotAMethodGroup_Throws()
    {
        var act = () => this.BuildWithOperations(collection =>
            collection.Operation<ItemsToGroup>("GROUP", x => argument => x.GroupItems(argument)));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Patch operation registration must select an instance method group*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CustomOperation_WhenNameIsBlank_Throws(string operation)
    {
        var act = () => this.BuildWithOperations(collection => collection.Operation<ItemsToGroup>(operation, x => x.GroupItems));

        act.Should().Throw<ArgumentException>();
    }

    private void BuildWithOperations(Action<KVCollectionOptionsBuilder<PatchErrorModel, ChangeSetPatchItemNode>> configure)
    {
        this.RegisterModelDefinition<PatchErrorModel>(builder => builder.Collection(x => x.Items, collection =>
        {
            configure(collection);
            collection.Item<ChangeSetPatchItemNode>(item => item.Field(x => x.Name));
        }));
        _ = this.CreateRegistry().Get<PatchErrorModel>();
    }
}

public partial class PatchErrorModel : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Choice")]
    public partial FieldEnum Choice { get; set; }

    [KVBind("Items")]
    public KVCollectionNode<ChangeSetPatchItemNode> Items { get; } = new();

    public List<int> Bumps { get; } = [];

    public List<ItemsToGroup?> Notes { get; } = [];

    public void GroupItems(ItemsToGroup argument) => this.Items.Create(argument.ItemId);

    public void Note(ItemsToGroup argument) => this.Notes.Add(argument);

    public void Bump(int amount) => this.Bumps.Add(amount);
}
