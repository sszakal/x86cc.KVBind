using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class NodeOperationTests : KVModelTestBase
{
    public NodeOperationTests()
    {
        this.RegisterModelDefinition<OpContract>(builder =>
        {
            builder.Field(x => x.Title);
            builder.Field(x => x.Status, f => f.Default("draft").PatchReadOnly());

            builder.Operation(x => x.Submit);
            builder.Operation<string>("CHOOSE_GRANT", x => x.ChooseGrant);
            builder.Operation<OpRename>("RENAME", x => x.Rename);

            builder.FieldGroup(
                x => x.Terms,
                terms =>
                {
                    terms.Field(x => x.Years);
                    terms.Operation<int>("EXTEND", x => x.Extend);
                });

            builder.NestedNode(x => x.Grant, grant =>
            {
                grant.Bind<OpExclusiveGrant>("EXCLUSIVE", exclusive =>
                {
                    exclusive.Field(x => x.Territory);
                    exclusive.Operation("WORLDWIDE", x => x.MakeWorldwide);
                });
                grant.Bind<OpLicenceGrant>("LICENCE", licence => licence.Field(x => x.Term));
            });

            builder.Collection(x => x.Approvals, approvals => approvals
                .Item<OpApproval>(item =>
                {
                    item.Field(x => x.State, f => f.Default("pending"));
                    item.Operation(x => x.Approve);
                })
                .Seed<OpApproval>("legal")
                .AllowOperations()
                .PatchReadOnly());
        });
    }

    [Fact]
    public void RootOperation_InvokesTheDomainMethod_AndMayChangePatchReadOnlyMembers()
    {
        var contract = this.CreateNew();

        var set = () => contract.Patch(KVPatchOperation.Set("/Status", "submitted"));
        set.Should().Throw<InvalidOperationException>();

        contract.Patch(KVPatchOperation.Custom("SUBMIT", "/"));

        contract.Status.Should().Be("submitted");
    }

    [Fact]
    public void RootOperation_WithAnArgument_CanSwitchANestedNodeThroughDomainLogic()
    {
        var contract = this.CreateNew();

        contract.Patch(
            KVPatchOperation.Custom("CHOOSE_GRANT", "/", "EXCLUSIVE"),
            KVPatchOperation.Set("/Grant/Territory", "EU"));

        contract.Grant.Should().BeOfType<OpExclusiveGrant>().Which.Territory.Should().Be("EU");

        contract.Patch(KVPatchOperation.Custom("choose_grant", "/", "LICENCE"));

        contract.Grant.Should().BeOfType<OpLicenceGrant>();
    }

    [Fact]
    public void Operation_ConvertsAJsonPayloadToTheArgumentType()
    {
        var contract = this.CreateNew();
        var payload = JsonDocument.Parse("""{ "title": "Master Agreement", "reason": "typo" }""").RootElement;

        contract.Patch(KVPatchOperation.Custom("RENAME", "/", payload));

        contract.Title.Should().Be("Master Agreement (typo)");
    }

    [Fact]
    public void FieldGroupOperation_IsAddressedByTheGroupPath()
    {
        var contract = this.CreateNew();
        contract.Terms.Years = 2;

        contract.Patch(KVPatchOperation.Custom("EXTEND", "/Terms", 3));

        contract.Terms.Years.Should().Be(5);
    }

    [Fact]
    public void NestedNodeOperation_RunsOnTheActiveSubtype()
    {
        var contract = this.CreateNew();
        contract.ChooseGrant("EXCLUSIVE");

        contract.Patch(KVPatchOperation.Custom("WORLDWIDE", "/Grant"));

        ((OpExclusiveGrant)contract.Grant!).Territory.Should().Be("WORLD");
    }

    [Fact]
    public void NestedNodeOperation_IsRejected_WhenTheSlotIsEmptyOrHoldsAnotherSubtype()
    {
        var contract = this.CreateNew();

        var empty = () => contract.Patch(KVPatchOperation.Custom("WORLDWIDE", "/Grant"));
        empty.Should().Throw<InvalidOperationException>().WithMessage("*'WORLDWIDE' is not valid for path '/Grant'*");

        contract.ChooseGrant("LICENCE");
        var other = () => contract.Patch(KVPatchOperation.Custom("WORLDWIDE", "/Grant"));
        other.Should().Throw<InvalidOperationException>().WithMessage("*'WORLDWIDE' is not valid for path '/Grant'*");
    }

    [Fact]
    public void CollectionItemOperation_Runs_EvenWhenTheCollectionIsLockedAndPatchReadOnly()
    {
        var contract = this.CreateNew();

        var set = () => contract.Patch(KVPatchOperation.Set("/Approvals/legal/State", "approved"));
        set.Should().Throw<InvalidOperationException>().WithMessage("*'Approvals' is patch read-only.");

        contract.Patch(KVPatchOperation.Custom("APPROVE", "/Approvals/legal"));

        contract.Approvals.GetById("legal")!.State.Should().Be("approved");
    }

    [Fact]
    public void UnknownOperation_IsRejected()
    {
        var contract = this.CreateNew();

        var root = () => contract.Patch(KVPatchOperation.Custom("ARCHIVE", "/"));
        var group = () => contract.Patch(KVPatchOperation.Custom("SUBMIT", "/Terms"));

        root.Should().Throw<InvalidOperationException>().WithMessage("*'ARCHIVE' is not valid for path '/'*");
        group.Should().Throw<InvalidOperationException>().WithMessage("*'SUBMIT' is not valid for path '/Terms'*");
    }

    [Fact]
    public void Operation_RejectsAnUnexpectedOrMissingValue()
    {
        var contract = this.CreateNew();

        var unexpected = () => contract.Patch(KVPatchOperation.Custom("SUBMIT", "/", "now"));
        var missing = () => contract.Patch(KVPatchOperation.Custom("EXTEND", "/Terms"));

        unexpected.Should().Throw<InvalidOperationException>().WithMessage("*'SUBMIT' for '/' does not accept a value.");
        missing.Should().Throw<InvalidOperationException>().WithMessage("*'EXTEND' for '/Terms' requires a 'System.Int32' payload.");
    }

    [Fact]
    public void Definition_ExposesOperationNames_AndRejectsBuiltInOrDuplicateNames()
    {
        var definition = this.CreateRegistry().Get<OpContract>();

        definition.OperationNames.Should().BeEquivalentTo("SUBMIT", "CHOOSE_GRANT", "RENAME");
        definition.FindNode("Terms")!.OperationNames.Should().BeEquivalentTo("EXTEND");

        var builtIn = () => new KVBindBuilder<OpContract>().Operation("RESET", x => x.Submit);
        var duplicate = () =>
        {
            var builder = new KVBindBuilder<OpContract>();
            builder.Operation(x => x.Submit);
            builder.Operation("submit", x => x.Submit);
        };

        builtIn.Should().Throw<InvalidOperationException>().WithMessage("*'RESET' is built in*");
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*'SUBMIT' is already registered*");
    }

    private OpContract CreateNew()
    {
        var contract = this.CreateRoot<OpContract>();
        contract.ApplyDefaults();
        return contract;
    }
}

public sealed record OpRename(string Title, string Reason);

public partial class OpContract : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Status")]
    public partial string? Status { get; set; }

    [KVBind("Terms")]
    public OpTerms Terms { get; } = new();

    [KVBind("Grant")]
    public partial OpGrant? Grant { get; private set; }

    [KVBind("Approvals")]
    public KVCollectionNode<OpApproval> Approvals { get; } = new();

    public void Submit() => this.Status = "submitted";

    public void ChooseGrant(string kind)
        => this.Grant = kind == "EXCLUSIVE" ? new OpExclusiveGrant() : new OpLicenceGrant();

    public void Rename(OpRename rename) => this.Title = $"{rename.Title} ({rename.Reason})";
}

public partial class OpTerms : KVFieldGroupNode
{
    [KVBind("Years")]
    public partial int Years { get; set; }

    public void Extend(int years) => this.Years += years;
}

public abstract partial class OpGrant : KVNestedNode;

public partial class OpExclusiveGrant : OpGrant
{
    [KVBind("Territory")]
    public partial string? Territory { get; set; }

    public void MakeWorldwide() => this.Territory = "WORLD";
}

public partial class OpLicenceGrant : OpGrant
{
    [KVBind("Term")]
    public partial string? Term { get; set; }
}

public partial class OpApproval : KVCollectionItemNode
{
    [KVBind("State")]
    public partial string? State { get; set; }

    public void Approve() => this.State = "approved";
}
