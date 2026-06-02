using AwesomeAssertions;
using x86cc.KVBind.Core;

namespace x86cc.KVBind.UnitTests.Core;

public class ChangeReactionTests : KVModelTestBase
{
    public ChangeReactionTests()
    {
        RegisterModelDefinition<ReactionRoot>(builder =>
        {
            builder.Field(x => x.Summary);
            builder.Collection(x => x.Items, collection =>
            {
                collection.Item<ReactionItem>(item =>
                {
                    item.Field(x => x.Amount);
                    item.NestedNode(x => x.Detail, nested =>
                    {
                        nested.Bind<ReactionDetail>(detail => detail.Field(x => x.Code));
                    });
                });
            });
            builder.NestedNode(x => x.Detail, nested =>
            {
                nested.Bind<ReactionDetail>(detail => detail.Field(x => x.Code));
            });
            builder.OnChange(path => path.Collection(x => x.Items).Field(x => x.Amount), x => x.ResetSummary);
            builder.OnChange(path => path.Collection(x => x.Items).Any(), x => x.TrackCollectionChange);
            builder.OnChange(path => path.NestedNode(x => x.Detail).Field(x => x.Code), x => x.ResetSummary);
            builder.OnChange(path => path.Collection(x => x.Items).NestedNode(x => x.Detail).Field(x => x.Code), x => x.ResetSummary);
        });

        RegisterModelDefinition<DirectCycleReactionRoot>(builder =>
        {
            builder.Field(x => x.A);
            builder.OnChange(path => path.Field(x => x.A), x => x.ChangeAAgain);
        });

        RegisterModelDefinition<IndirectCycleReactionRoot>(builder =>
        {
            builder.Field(x => x.A);
            builder.Field(x => x.B);
            builder.Field(x => x.C);
            builder.OnChange(path => path.Field(x => x.A), x => x.ChangeB);
            builder.OnChange(path => path.Field(x => x.B), x => x.ChangeA);
        });
    }

    [Fact]
    public void DirectSetter_WhenCollectionItemFieldChanges_TriggersRootReaction()
    {
        var root = CreateRoot<ReactionRoot>();
        var itemId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        root.Items.Create(itemId).Amount = 12;

        root.Summary.Should().Be($"Items/{itemId:D}/Amount:12");
        root.CollectionChanges.Should().ContainSingle().Which.Should().Be($"Items/{itemId:D}:added");
    }

    [Fact]
    public void PatchSet_WhenCollectionItemFieldChanges_TriggersRootReaction()
    {
        var root = CreateRoot<ReactionRoot>();
        var itemId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        root.Patch(KVPatchOperation.Add("/Items", new KVAddPatchPayload(itemId)));

        root.Patch(KVPatchOperation.Set($"/Items/{itemId:D}/Amount", 34));

        root.Summary.Should().Be($"Items/{itemId:D}/Amount:34");
    }

    [Fact]
    public void PatchSet_WhenNestedNodeFieldChanges_TriggersRootReaction()
    {
        var root = CreateRoot<ReactionRoot>();
        root.Patch(KVPatchOperation.Init("/Detail", nameof(ReactionDetail)));

        root.Patch(KVPatchOperation.Set("/Detail/Code", "A"));

        root.Summary.Should().Be("Detail/Code:A");
    }

    [Fact]
    public void PatchSet_WhenNestedCollectionNestedNodeFieldChanges_TriggersRootReaction()
    {
        var root = CreateRoot<ReactionRoot>();
        var itemId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        root.Patch(
            KVPatchOperation.Add("/Items", new KVAddPatchPayload(itemId)),
            KVPatchOperation.Init($"/Items/{itemId:D}/Detail", nameof(ReactionDetail)));

        root.Patch(KVPatchOperation.Set($"/Items/{itemId:D}/Detail/Code", "B"));

        root.Summary.Should().Be($"Items/{itemId:D}/Detail/Code:B");
    }

    [Fact]
    public void DirectSetter_WhenUnrelatedFieldChanges_DoesNotTriggerRootReaction()
    {
        var root = CreateRoot<ReactionRoot>();

        root.Summary = "manual";

        root.Summary.Should().Be("manual");
    }

    [Fact]
    public void CollectionRemove_WhenItemIsRemoved_TriggersCollectionReaction()
    {
        var root = CreateRoot<ReactionRoot>();
        var itemId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        root.Items.Create(itemId);

        root.Items.RemoveById(itemId.ToString("D"));

        root.CollectionChanges.Should().Equal(
            $"Items/{itemId:D}:added",
            $"Items/{itemId:D}:removed");
    }

    [Fact]
    public void DirectCycle_WhenReactionChangesSameField_ThrowsCycleError()
    {
        var root = CreateRoot<DirectCycleReactionRoot>();

        var act = () => root.A = "start";

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cyclic change reaction detected*");
    }

    [Fact]
    public void IndirectCycle_WhenReactionsChangeEachOther_ThrowsCycleErrorAndCleansRootState()
    {
        var root = CreateRoot<IndirectCycleReactionRoot>();

        var act = () => root.A = "start";

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cyclic change reaction detected*");

        var unrelated = () => root.C = "after-cycle";
        unrelated.Should().NotThrow();
        root.C.Should().Be("after-cycle");
    }
}

public class ReactionRoot : KVRootNode
{
    public KVCollectionNode<ReactionItem> Items { get; } = new();

    public List<string> CollectionChanges { get; } = [];

    public ReactionDetail? Detail
    {
        get => GetNestedNode<ReactionDetail>(nameof(Detail));
        set => SetNestedNode(nameof(Detail), value);
    }

    public string? Summary
    {
        get => GetField<string?>(nameof(Summary));
        set => SetField(nameof(Summary), value);
    }

    public void ResetSummary(KVChangeContext<ReactionRoot> context)
    {
        Summary = $"{context.ChangedPath}:{context.NewValue}";
    }

    public void TrackCollectionChange(KVChangeContext<ReactionRoot> context)
    {
        CollectionChanges.Add($"{context.ChangedPath}:{(context.NewValue is null ? "removed" : "added")}");
    }
}

public class ReactionItem : KVCollectionItemNode
{
    public int Amount
    {
        get => GetField<int>(nameof(Amount));
        set => SetField(nameof(Amount), value);
    }

    public ReactionDetail? Detail
    {
        get => GetNestedNode<ReactionDetail>(nameof(Detail));
        set => SetNestedNode(nameof(Detail), value);
    }
}

public class ReactionDetail : KVNestedNode
{
    public string? Code
    {
        get => GetField<string?>(nameof(Code));
        set => SetField(nameof(Code), value);
    }
}

public class DirectCycleReactionRoot : KVRootNode
{
    public string? A
    {
        get => GetField<string?>(nameof(A));
        set => SetField(nameof(A), value);
    }

    public void ChangeAAgain(KVChangeContext<DirectCycleReactionRoot> context)
    {
        A = $"{context.NewValue}!";
    }
}

public class IndirectCycleReactionRoot : KVRootNode
{
    public string? A
    {
        get => GetField<string?>(nameof(A));
        set => SetField(nameof(A), value);
    }

    public string? B
    {
        get => GetField<string?>(nameof(B));
        set => SetField(nameof(B), value);
    }

    public string? C
    {
        get => GetField<string?>(nameof(C));
        set => SetField(nameof(C), value);
    }

    public void ChangeB(KVChangeContext<IndirectCycleReactionRoot> context)
    {
        B = $"{context.NewValue}:B";
    }

    public void ChangeA(KVChangeContext<IndirectCycleReactionRoot> context)
    {
        A = $"{context.NewValue}:A";
    }
}
