using System.Text.Json;
using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.IntegrationTests.Fixtures;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.DeepForms;

namespace x86cc.KVBind.IntegrationTests;

// The recursive component tree through the API against Postgres: a four-level tree is stored and read
// back, change rows are labelled deep in the tree, and two drafts that changed different leaves rebase
// cleanly although both rewrote every subtotal above them.
[Collection(SampleApiCollection.Name)]
public sealed class DeepFormApiTests(SampleApiFixture fixture)
{
    private static readonly DeepFormShape Shape = DeepFormShape.AtDepth(4);

    private IAggregateModel Deep => fixture.Model(DeepFormModels.Name);

    [Fact]
    public async Task AFourLevelTree_IsStoredAndReadBack_WithItsRolledUpTotal()
    {
        var id = await this.CreateTreeAsync();

        var snapshot = (await fixture.Service().GetSnapshotAsync(this.Deep, id, default))!;

        snapshot.Data["TotalCost"]!.GetValue<decimal>().Should().Be(DeepFormFiller.ExpectedTotal(Shape));
        snapshot.Computed["Components"].Should().Be(Shape.Components);
        snapshot.Computed["Depth"].Should().Be(4);
        var leaf = snapshot.Data["Components"]![0]!["Detail"]!["Components"]![0]!["Detail"]!["Components"]![0]!["Detail"]!["Components"]![0]!;
        leaf["$id"]!.GetValue<string>().Should().Be("c1-1-1-1");
        leaf["Detail"]!["Price"]!.GetValue<decimal>().Should().Be(10m);
    }

    [Fact]
    public async Task ADeepChange_IsListedWithLabels_AlongWithTheTotalsItRecomputed()
    {
        var id = await this.CreateTreeAsync();
        var draft = await this.OpenAsync(id, "alice");
        var leaf = DeepFormFiller.DeepestLeaf(Shape);

        var response = await this.PatchAsync(id, draft, Set($"{leaf.Path}/Category", "electrical"), Set($"{leaf.Path}/Detail/Price", 110m));

        response.Changes.Should().Contain(row => row.Path == $"Components/c3/Detail/Components/c3-2/Detail/Components/c3-2-2/Detail/Components/c3-2-2-2/Category"
                                                 && Equals(row.NewValue, "Electrical"));
        response.Changes.Should().Contain(row => row.Path == "Total Cost");
        response.Changes.Count(row => row.Path.EndsWith("/Subtotal", StringComparison.Ordinal)).Should().Be(4, "the purchased part and the three assemblies above it");
        response.Data["TotalCost"]!.GetValue<decimal>().Should().Be(DeepFormFiller.ExpectedTotal(Shape) + 100m);
    }

    [Fact]
    public async Task DraftsChangingDifferentLeaves_Rebase_WithoutConflictsOnTheDerivedTotals()
    {
        var id = await this.CreateTreeAsync();
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");

        await this.PatchAsync(id, alice, Set("/Components/c1/Detail/Components/c1-1/Detail/Components/c1-1-1/Detail/Components/c1-1-1-1/Detail/Price", 20m));
        await this.PatchAsync(id, bob, Set("/Components/c3/Detail/Components/c3-2/Detail/Components/c3-2-2/Detail/Components/c3-2-2-2/Detail/Price", 40m));
        await this.CommitAsync(id, alice, "alice");

        var rebase = await fixture.Service().BeginRebaseAsync(this.Deep, id, bob, default);

        rebase!.Outcome.Should().Be("CanAutomerge", "the only overlapping paths, TotalCost and the subtotals, are derived");
        await fixture.Service().FinishRebaseAsync(this.Deep, id, bob, default);
        await this.CommitAsync(id, bob, "bob");

        var data = (await fixture.Service().GetSnapshotAsync(this.Deep, id, default))!.Data;
        data["TotalCost"]!.GetValue<decimal>().Should().Be(DeepFormFiller.ExpectedTotal(Shape) + 10m + 30m);
    }

    [Fact]
    public async Task History_IsReadAPageAtATime_NewestFirst()
    {
        var created = await fixture.Service().CreateAsync(
            this.Deep,
            new CreateAggregateRequest("setup", new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["Title"] = JsonSerializer.SerializeToElement("Paged") }),
            default);
        for (var i = 1; i <= 7; i++)
        {
            var draft = await this.OpenAsync(created.Id, "writer");
            await this.PatchAsync(created.Id, draft, Set("/Title", $"Edit {i}"));
            await this.CommitAsync(created.Id, draft, "writer");
        }

        var first = await fixture.Service().ListChangeSetsAsync(this.Deep, created.Id, 0, 3, default);
        var second = await fixture.Service().ListChangeSetsAsync(this.Deep, created.Id, 3, 3, default);
        var last = await fixture.Service().ListChangeSetsAsync(this.Deep, created.Id, 6, 3, default);

        first.Total.Should().Be(8, "the create commit and seven edits");
        first.Items.Should().HaveCount(3);
        last.Items.Should().HaveCount(2);
        var all = first.Items.Concat(second.Items).Concat(last.Items).ToList();
        all.Select(c => c.CommitId).Should().OnlyHaveUniqueItems();
        all.Select(c => c.Timestamp).Should().BeInDescendingOrder();
        all[0].PreviousCommitId.Should().Be(all[1].CommitId, "newest first, each page continuing the chain");
        (await fixture.Service().ListChangeSetsAsync(this.Deep, created.Id, 0, 10_000, default)).Limit.Should().Be(AggregateService.MaxChangeSetPage);
    }

    private static PatchOperationRequest Set(string path, object value) => new("SET", path, JsonSerializer.SerializeToElement(value));

    private static PatchOperationRequest ToRequest(KVPatchOperation operation) => operation.Value switch
    {
        KVAddPatchPayload add => new(operation.OperationCode, operation.Path, JsonSerializer.SerializeToElement(new { itemId = add.ItemId, typeToken = add.TypeToken })),
        null => new(operation.OperationCode, operation.Path),
        var value => new(operation.OperationCode, operation.Path, JsonSerializer.SerializeToElement(value, value.GetType())),
    };

    private async Task<Guid> CreateTreeAsync()
    {
        var created = await fixture.Service().CreateAsync(
            this.Deep,
            new CreateAggregateRequest("setup", new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["Title"] = JsonSerializer.SerializeToElement("Tree") }),
            default);
        var draft = await this.OpenAsync(created.Id, "setup");
        await this.PatchAsync(created.Id, draft, [.. DeepFormFiller.Build(Shape).Select(ToRequest)]);
        await this.CommitAsync(created.Id, draft, "setup");
        return created.Id;
    }

    private async Task<Guid> OpenAsync(Guid id, string user) =>
        (await fixture.Service().OpenDraftAsync(this.Deep, id, new OpenDraftRequest(user), default))!.DraftId;

    private async Task<AggregateDraftResponse> PatchAsync(Guid id, Guid draft, params PatchOperationRequest[] operations) =>
        (await fixture.Service().PatchDraftAsync(this.Deep, id, draft, operations, default))!;

    private async Task CommitAsync(Guid id, Guid draft, string user)
    {
        var result = await fixture.Service().CommitDraftAsync(this.Deep, id, draft, new CommitDraftRequest(user), default);
        result!.Invalid.Should().BeNull(result.Invalid is null ? string.Empty : string.Join("; ", result.Invalid.Errors.Select(e => $"{e.Path} {e.Code}")));
        result.StaleDraft.Should().BeNull();
    }
}
