using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;

namespace x86cc.KVBind.UnitTests.Core;

public class ReplacedChangeReactionTests : KVModelTestBase
{
    public ReplacedChangeReactionTests()
    {
        this.RegisterModelDefinition<RplOrder>(builder =>
        {
            builder.Field(x => x.Title);
            builder.Field(x => x.Total);

            builder.Collection(x => x.Lines, lines => lines.Item<RplLine>(line =>
            {
                line.Field(x => x.Quantity);
                line.Field(x => x.Price);
                line.Field(x => x.LineTotal);
                line.OnChange(path => path.Field(x => x.Quantity), x => x.RecalculateLineTotal);
                line.OnChange(path => path.Field(x => x.Price), x => x.RecalculateLineTotal);
            }));

            builder.OnChange(path => path.Collection(x => x.Lines).Field(x => x.LineTotal), x => x.RecalculateTotal);
            builder.OnChange(path => path.Collection(x => x.Lines).Any(), x => x.RecalculateTotal);
            builder.OnChange(path => path.Field(x => x.Title), x => x.TrackTitle);
        });
    }

    [Fact]
    public void DiscardPatch_OfASourceField_RecomputesDerivedValues()
    {
        var order = this.CreateCommitted();
        order.Lines.GetById("a")!.Quantity = 5m;
        order.Total.Should().Be(55m);

        order.Patch(KVPatchOperation.Discard("/Lines/a/Quantity"));

        order.Lines.GetById("a")!.LineTotal.Should().Be(20m);
        order.Total.Should().Be(25m);
    }

    [Fact]
    public void DiscardPatch_OfACollection_RecomputesTheTotalOverIt()
    {
        var order = this.CreateCommitted();
        var added = order.Lines.Create("c");
        added.Quantity = 1m;
        added.Price = 100m;
        order.Total.Should().Be(125m);

        order.Discard("/Lines");

        order.Lines.GetActiveItemIds().Should().Equal("a", "b");
        order.Total.Should().Be(25m);
    }

    [Fact]
    public void Clear_RecomputesDerivedValues_WithoutLeavingDraftChanges()
    {
        var order = this.CreateCommitted();
        order.Lines.GetById("b")!.Price = 50m;
        order.Total.Should().Be(70m);

        order.Clear();

        order.Total.Should().Be(25m);
        order.GetAllChanges().Changes.Should().BeEmpty();
    }

    [Fact]
    public void ResetPatch_OfAnItem_RecomputesItsDerivedValueAndTheTotal()
    {
        var order = this.CreateCommitted();

        order.Patch(KVPatchOperation.Reset("/Lines/a"));

        order.Lines.GetById("a")!.LineTotal.Should().Be(0m);
        order.Total.Should().Be(5m);
    }

    [Fact]
    public void NotifyReplaced_AfterTheOverlayChangedDirectly_RecomputesDerivedValues()
    {
        var order = this.CreateCommitted();

        // What a finished rebase does: values change in the overlay without going through the aggregate.
        order.RootModel().Overlay.Set("Lines/a/Quantity", new KVValue<decimal>(7m));
        order.Total.Should().Be(25m);

        order.NotifyReplaced();

        order.Lines.GetById("a")!.LineTotal.Should().Be(70m);
        order.Total.Should().Be(75m);
    }

    [Fact]
    public void Replaced_RunsOnlyReactionsWatchingTheReplacedPath_OncePerReaction()
    {
        var order = this.CreateCommitted();
        order.Title = "edited";
        order.TitleLog.Clear();

        order.Discard("/Lines");
        order.TitleLog.Should().BeEmpty();

        order.Discard("/Title");
        order.TitleLog.Should().Equal("Title:Replaced:<null>");

        order.Clear();
        order.TitleLog.Should().Equal("Title:Replaced:<null>", ":Replaced:<null>");
    }

    private RplOrder CreateCommitted()
    {
        var order = this.CreateRoot<RplOrder>();
        var a = order.Lines.Create("a");
        a.Quantity = 2m;
        a.Price = 10m;
        var b = order.Lines.Create("b");
        b.Quantity = 1m;
        b.Price = 5m;
        order.Total.Should().Be(25m);
        order.CommitSetup();
        return order;
    }
}

public partial class RplOrder : KVRootNode
{
    public List<string> TitleLog { get; } = [];

    [KVBind("Title")]
    public partial string? Title { get; set; }

    [KVBind("Total")]
    public partial decimal Total { get; set; }

    [KVBind("Lines")]
    public KVCollectionNode<RplLine> Lines { get; } = new();

    public void RecalculateTotal(KVChangeContext<RplOrder> context)
        => this.Total = this.Lines.Sum(line => line.LineTotal);

    public void TrackTitle(KVChangeContext<RplOrder> context)
        => this.TitleLog.Add($"{context.ChangedPath}:{context.Kind}:{context.NewValue ?? "<null>"}");
}

public partial class RplLine : KVCollectionItemNode
{
    [KVBind("Quantity")]
    public partial decimal Quantity { get; set; }

    [KVBind("Price")]
    public partial decimal Price { get; set; }

    [KVBind("LineTotal")]
    public partial decimal LineTotal { get; set; }

    public void RecalculateLineTotal(KVChangeContext<RplLine> context)
        => this.LineTotal = this.Quantity * this.Price;
}
