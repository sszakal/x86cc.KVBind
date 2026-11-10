using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class PatchReadOnlyTests : KVModelTestBase
{
    public PatchReadOnlyTests()
    {
        this.RegisterModelDefinition<ProRoot>(builder =>
        {
            builder.Field(x => x.Title);
            builder.Field(x => x.Status, f => f.PatchReadOnly());

            builder.FieldGroup(x => x.Audit, audit => audit.Field(x => x.Note), group => group.PatchReadOnly());
            builder.FieldGroup(
                x => x.Settings,
                settings =>
                {
                    settings.Field(x => x.Note);
                    settings.Field(x => x.Locked, f => f.PatchReadOnly());
                },
                group => group.Resettable());

            builder.Collection(x => x.Lines, lines => lines.Item<ProLine>(item =>
            {
                item.Field(x => x.Text);
                item.Field(x => x.State, f => f.Default("new").PatchReadOnly());
            }));

            builder.Collection(x => x.Approvals, approvals => approvals
                .Item<ProLine>(item => item.Field(x => x.Text))
                .PatchReadOnly());

            builder.NestedNode(x => x.Party, party => party.Bind<ProPerson>("PERSON", person =>
            {
                person.Field(x => x.Name);
                person.Field(x => x.Verified, f => f.Default("no").PatchReadOnly());
            }));

            builder.NestedNode(x => x.Approver, approver => approver
                .Bind<ProPerson>("PERSON", person => person.Field(x => x.Name))
                .PatchReadOnly());
        });
    }

    [Fact]
    public void Definition_ExposesPatchReadOnlyMembers()
    {
        var definition = this.CreateRegistry().Get<ProRoot>();

        definition.FindField("Title")!.IsPatchReadOnly.Should().BeFalse();
        definition.FindField("Status")!.IsPatchReadOnly.Should().BeTrue();
        definition.FindNode("Audit")!.IsPatchReadOnly.Should().BeTrue();
        definition.FindCollection("Approvals")!.IsPatchReadOnly.Should().BeTrue();
        definition.FindNestedNode("Approver")!.IsPatchReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Field_RejectsSetAndUnsetPatches_ButStaysWritableInCode()
    {
        var root = this.CreateCommitted(r => r.Status = "draft");

        var set = () => root.Patch(KVPatchOperation.Set("/Status", "approved"));
        var unset = () => root.Patch(KVPatchOperation.Unset("/Status"));

        set.Should().Throw<InvalidOperationException>().WithMessage("*'Status' is patch read-only.");
        unset.Should().Throw<InvalidOperationException>().WithMessage("*'Status' is patch read-only.");
        root.Status.Should().Be("draft");

        root.Status = "approved";
        root.Status.Should().Be("approved");
    }

    [Fact]
    public void Group_RejectsPatchesBeneathIt()
    {
        var root = this.CreateCommitted();

        var act = () => root.Patch(KVPatchOperation.Set("/Audit/Note", "x"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Audit' is patch read-only.");
        root.Audit.Note = "from code";
        root.Audit.Note.Should().Be("from code");
    }

    [Fact]
    public void Collection_RejectsStructuralAndFieldPatches_ButStaysWritableInCode()
    {
        var root = this.CreateCommitted(r => r.Approvals.Create("a1").Text = "ok");

        var add = () => root.Patch(KVPatchOperation.Add("/Approvals", new KVAddPatchPayload("a2")));
        var set = () => root.Patch(KVPatchOperation.Set("/Approvals/a1/Text", "changed"));
        var remove = () => root.Patch(KVPatchOperation.Remove("/Approvals/a1"));

        add.Should().Throw<InvalidOperationException>().WithMessage("*'Approvals' is patch read-only.");
        set.Should().Throw<InvalidOperationException>().WithMessage("*'Approvals' is patch read-only.");
        remove.Should().Throw<InvalidOperationException>().WithMessage("*'Approvals' is patch read-only.");

        root.Approvals.Create("a2");
        root.Approvals.GetActiveItemIds().Should().Equal("a1", "a2");
    }

    [Fact]
    public void NestedNode_RejectsInitDropAndFieldPatches()
    {
        var root = this.CreateCommitted(r => r.AssignApprover("Ann"));

        var init = () => root.Patch(KVPatchOperation.Init("/Approver", "PERSON"));
        var drop = () => root.Patch(KVPatchOperation.Drop("/Approver"));
        var set = () => root.Patch(KVPatchOperation.Set("/Approver/Name", "Bob"));

        init.Should().Throw<InvalidOperationException>().WithMessage("*'Approver' is patch read-only.");
        drop.Should().Throw<InvalidOperationException>().WithMessage("*'Approver' is patch read-only.");
        set.Should().Throw<InvalidOperationException>().WithMessage("*'Approver' is patch read-only.");
        root.Approver!.Name.Should().Be("Ann");
    }

    [Fact]
    public void FieldInsideCollectionItem_IsProtected_WhileTheItemLifecycleStaysPatchable()
    {
        var root = this.CreateCommitted();

        root.Patch(
            KVPatchOperation.Add("/Lines", new KVAddPatchPayload("l1")),
            KVPatchOperation.Set("/Lines/l1/Text", "hello"));
        var set = () => root.Patch(KVPatchOperation.Set("/Lines/l1/State", "done"));

        set.Should().Throw<InvalidOperationException>().WithMessage("*'Lines/l1/State' is patch read-only.");
        root.Lines.GetById("l1")!.State.Should().Be("new");

        root.Patch(KVPatchOperation.Remove("/Lines/l1"));
        root.Lines.GetActiveItemIds().Should().BeEmpty();
    }

    [Fact]
    public void Reset_IsRejected_WhenTheNodeContainsPatchReadOnlyMembers()
    {
        var root = this.CreateCommitted(r =>
        {
            r.Lines.Create("l1");
            r.Settings.Locked = "yes";
        });
        root.Patch(KVPatchOperation.Init("/Party", "PERSON"));

        var item = () => root.Patch(KVPatchOperation.Reset("/Lines/l1"));
        var group = () => root.Patch(KVPatchOperation.Reset("/Settings"));
        var nested = () => root.Patch(KVPatchOperation.Reset("/Party"));

        item.Should().Throw<InvalidOperationException>().WithMessage("*'Lines/l1' contains patch read-only members.");
        group.Should().Throw<InvalidOperationException>().WithMessage("*'Settings' contains patch read-only members.");
        nested.Should().Throw<InvalidOperationException>().WithMessage("*'Party' contains patch read-only members.");
        root.Settings.Locked.Should().Be("yes");
    }

    [Fact]
    public void Discard_IsRejected_WhenItWouldRevertAPatchReadOnlyChange()
    {
        var root = this.CreateCommitted(r => r.Status = "draft");
        root.Status = "approved";
        root.Title = "edited";

        var exact = () => root.Patch(KVPatchOperation.Discard("/Status"));
        var all = () => root.Patch(KVPatchOperation.Discard("/"));

        exact.Should().Throw<InvalidOperationException>().WithMessage("*'Status' is patch read-only.");
        all.Should().Throw<InvalidOperationException>().WithMessage("*'Status' is patch read-only.");
        root.Status.Should().Be("approved");

        root.Patch(KVPatchOperation.Discard("/Title"));
        root.Title.Should().BeNull();
        root.Status.Should().Be("approved");
    }

    [Fact]
    public void Discard_IsRejected_BeneathAPatchReadOnlyGroup()
    {
        var root = this.CreateCommitted();
        root.Audit.Note = "from code";

        var act = () => root.Patch(KVPatchOperation.Discard("/Audit/Note"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*'Audit' is patch read-only.");
    }

    [Fact]
    public void Discard_IsAllowed_WhenPatchReadOnlyMembersHaveNoDraftChanges()
    {
        var root = this.CreateCommitted(r => r.Status = "draft");
        root.Title = "edited";

        root.Patch(KVPatchOperation.Discard("/"));

        root.Title.Should().BeNull();
        root.Status.Should().Be("draft");
    }

    [Fact]
    public void Discard_IsAllowed_ForObjectsBornInTheDraft()
    {
        var root = this.CreateCommitted();
        root.Patch(
            KVPatchOperation.Add("/Lines", new KVAddPatchPayload("l1")),
            KVPatchOperation.Init("/Party", "PERSON"));
        root.Lines.GetById("l1")!.State.Should().Be("new");
        root.Party!.Verified.Should().Be("no");

        root.Patch(KVPatchOperation.Discard("/Party"));
        root.Patch(KVPatchOperation.Discard("/"));

        root.Party.Should().BeNull();
        root.Lines.GetActiveItemIds().Should().BeEmpty();
    }

    private ProRoot CreateCommitted(Action<ProRoot>? arrange = null)
    {
        var root = this.CreateRoot<ProRoot>();
        root.ApplyDefaults();
        arrange?.Invoke(root);
        root.CommitSetup();
        return root;
    }
}

public partial class ProRoot : KVRootNode
{
    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Status")]
    public partial string? Status { get; set; }

    [KVBind("Audit")]
    public ProNotes Audit { get; } = new();

    [KVBind("Settings")]
    public ProSettings Settings { get; } = new();

    [KVBind("Lines")]
    public KVCollectionNode<ProLine> Lines { get; } = new();

    [KVBind("Approvals")]
    public KVCollectionNode<ProLine> Approvals { get; } = new();

    [KVBind("Party")]
    public partial ProPerson? Party { get; private set; }

    [KVBind("Approver")]
    public partial ProPerson? Approver { get; private set; }

    public void AssignApprover(string name)
    {
        this.Approver = new ProPerson();
        this.Approver!.Name = name;
    }
}

public partial class ProNotes : KVFieldGroupNode
{
    [KVBind("Note")]
    public partial string? Note { get; set; }
}

public partial class ProSettings : KVFieldGroupNode
{
    [KVBind("Note")]
    public partial string? Note { get; set; }

    [KVBind("Locked")]
    public partial string? Locked { get; set; }
}

public partial class ProLine : KVCollectionItemNode
{
    [KVBind("Text")]
    public partial string? Text { get; set; }

    [KVBind("State")]
    public partial string? State { get; set; }
}

public partial class ProPerson : KVNestedNode
{
    [KVBind("Name")]
    public partial string? Name { get; set; }

    [KVBind("Verified")]
    public partial string? Verified { get; set; }
}
