using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class DomainEventTests : KVModelTestBase
{
    public DomainEventTests()
    {
        this.RegisterModelDefinition<EvtContract>(builder =>
        {
            builder.Field(x => x.Title);
            builder.Field(x => x.Status, f => f.Default("draft").PatchReadOnly());
            builder.Operation(x => x.Submit);
            builder.OnChange(path => path.Field(x => x.Title), x => x.OnTitleChanged);

            builder.FieldGroup(x => x.Terms, terms =>
            {
                terms.Field(x => x.Years);
                terms.Operation<int>("EXTEND", x => x.Extend);
            });

            builder.NestedNode(x => x.Grant, grant => grant.Bind<EvtGrant>("GRANT", g =>
            {
                g.Field(x => x.Territory);
                g.Operation("WORLDWIDE", x => x.MakeWorldwide);
            }));

            builder.Collection(x => x.Approvals, approvals => approvals
                .Item<EvtApproval>(item =>
                {
                    item.Field(x => x.State);
                    item.Operation(x => x.Approve);
                })
                .Seed<EvtApproval>("legal"));
        });
    }

    [Fact]
    public void DomainMethod_RaisesAnEvent_ThatIsDequeuedOnce()
    {
        var contract = this.CreateNew();

        contract.Submit();

        contract.PendingEvents.Should().Equal(new EvtSubmitted("draft"));
        contract.DequeueEvents().Should().Equal(new EvtSubmitted("draft"));
        contract.PendingEvents.Should().BeEmpty();
        contract.DequeueEvents().Should().BeEmpty();
    }

    [Fact]
    public void PatchOperation_RaisesTheSameEventAsTheDomainMethod()
    {
        var contract = this.CreateNew();

        contract.Patch(KVPatchOperation.Custom("SUBMIT", "/"));

        contract.DequeueEvents().Should().Equal(new EvtSubmitted("draft"));
    }

    [Fact]
    public void NodesBeneathTheRoot_RaiseOnTheRoot_InOrder()
    {
        var contract = this.CreateNew();

        contract.Patch(
            KVPatchOperation.Custom("EXTEND", "/Terms", 3),
            KVPatchOperation.Init("/Grant", "GRANT"),
            KVPatchOperation.Custom("WORLDWIDE", "/Grant"),
            KVPatchOperation.Custom("APPROVE", "/Approvals/legal"));

        contract.DequeueEvents().Should().Equal(
            new EvtTermExtended(3),
            new EvtGrantMadeWorldwide(),
            new EvtApproved("legal"));
    }

    [Fact]
    public void ChangeReaction_CanRaiseAnEvent_ForPatchesAndSettersAlike()
    {
        var contract = this.CreateNew();

        contract.Patch(KVPatchOperation.Set("/Title", "First"));
        contract.Title = "Second";

        contract.DequeueEvents().Should().Equal(new EvtRenamed("First"), new EvtRenamed("Second"));
    }

    [Fact]
    public void Clear_DropsPendingEventsWithTheDraft()
    {
        var contract = this.CreateNew();
        contract.Submit();

        contract.Clear();

        contract.PendingEvents.Should().BeEmpty();
    }

    [Fact]
    public void CreateCommit_LeavesEventsForTheApplicationToDequeue()
    {
        var contract = this.CreateNew();
        contract.Submit();

        contract.CreateCommit(DateTimeOffset.UtcNow);

        contract.DequeueEvents().Should().Equal(new EvtSubmitted("draft"));
    }

    [Fact]
    public void Raise_OnAnUnboundNode_Throws()
    {
        var act = () => new EvtApproval().Approve();

        act.Should().Throw<InvalidOperationException>().WithMessage("*is not bound*");
    }

    private EvtContract CreateNew()
    {
        var contract = this.CreateRoot<EvtContract>();
        contract.ApplyDefaults();
        return contract;
    }
}

public sealed record EvtSubmitted(string? PreviousStatus);

public sealed record EvtRenamed(string? Title);

public sealed record EvtTermExtended(int Years);

public sealed record EvtGrantMadeWorldwide;

public sealed record EvtApproved(string? ApprovalId);

public partial class EvtContract : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Status")]
    public partial string? Status { get; set; }

    [KVBind("Terms")]
    public EvtTerms Terms { get; } = new();

    [KVBind("Grant")]
    public partial EvtGrant? Grant { get; private set; }

    [KVBind("Approvals")]
    public KVCollectionNode<EvtApproval> Approvals { get; } = new();

    public void Submit()
    {
        var previous = this.Status;
        this.Status = "submitted";
        this.Raise(new EvtSubmitted(previous));
    }

    public void OnTitleChanged(KVChangeContext<EvtContract> context)
        => this.Raise(new EvtRenamed(this.Title));
}

public partial class EvtTerms : KVFieldGroupNode
{
    [KVBind("Years")]
    public partial int Years { get; set; }

    public void Extend(int years)
    {
        this.Years += years;
        this.Raise(new EvtTermExtended(years));
    }
}

public partial class EvtGrant : KVNestedNode
{
    [KVBind("Territory")]
    public partial string? Territory { get; set; }

    public void MakeWorldwide()
    {
        this.Territory = "WORLD";
        this.Raise(new EvtGrantMadeWorldwide());
    }
}

public partial class EvtApproval : KVCollectionItemNode
{
    [KVBind("State")]
    public partial string? State { get; set; }

    public void Approve()
    {
        this.State = "approved";
        this.Raise(new EvtApproved(this.ItemId()));
    }
}
