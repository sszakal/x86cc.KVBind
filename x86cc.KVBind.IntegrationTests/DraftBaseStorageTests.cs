using System.Text.Json;
using AwesomeAssertions;
using Marten;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.IntegrationTests.Fixtures;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.DeepForms;
using x86cc.KVBind.Sample.Api.Persistence;

namespace x86cc.KVBind.IntegrationTests;

// A stored draft holds its changes and the id of the commit it is based on, not a copy of the aggregate.
// The base is the aggregate's current snapshot while the draft is up to date; once another commit lands, the
// superseded snapshot is kept for as long as a draft refers to it.
[Collection(SampleApiCollection.Name)]
public sealed class DraftBaseStorageTests(SampleApiFixture fixture)
{
    private IAggregateModel Deep => fixture.Model(DeepFormModels.Name);

    [Fact]
    public async Task AStoredDraft_HoldsItsChangesOnly()
    {
        var id = await this.CreateAsync();
        var draft = await this.OpenAsync(id, "alice");
        await this.PatchAsync(id, draft, "Mine");

        var stored = await this.LoadDraftAsync(draft);

        stored.Snapshot.Should().BeNull();
        stored.Changes.Keys.Should().Equal("Title");
        stored.BaseCommitId.Should().Be((await fixture.Service().GetSnapshotAsync(this.Deep, id, default))!.LastCommitId);
        (await this.KeptBasesAsync(id)).Should().BeEmpty("the draft is based on the current snapshot");
    }

    [Fact]
    public async Task ADraftLeftBehind_KeepsWorkingOnTheSupersededSnapshot_UntilItRebases()
    {
        var id = await this.CreateAsync();
        var baseCommit = (await fixture.Service().GetSnapshotAsync(this.Deep, id, default))!.LastCommitId!.Value;
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, bob, "Bob's");

        await this.PatchAsync(id, alice, "Alice's");
        await this.CommitAsync(id, alice, "alice");

        // Bob's draft is behind now: its base is the kept copy of the snapshot it was opened on.
        (await this.KeptBasesAsync(id)).Should().Equal(baseCommit);
        var stale = await fixture.Service().GetDraftAsync(this.Deep, id, bob, default);
        stale!.IsStale.Should().BeTrue();
        stale.Data["Title"]!.GetValue<string>().Should().Be("Bob's");
        (await this.PatchAsync(id, bob, "Bob's, again")).Changes.Should().ContainSingle(row => Equals(row.OldValue, "Base"));

        var rebase = await fixture.Service().BeginRebaseAsync(this.Deep, id, bob, default);
        rebase!.Conflicts.Should().ContainSingle(c => c.Path == "Title" && c.RequiresResolution);
        await fixture.Service().ResolveRebaseConflictAsync(this.Deep, id, bob, new ResolveRebaseConflictRequest("Title", "Ours"), default);
        await fixture.Service().FinishRebaseAsync(this.Deep, id, bob, default);
        await this.CommitAsync(id, bob, "bob");

        (await fixture.Service().GetSnapshotAsync(this.Deep, id, default))!.Data["Title"]!.GetValue<string>().Should().Be("Bob's, again");
        (await this.KeptBasesAsync(id)).Should().NotContain(baseCommit, "no draft refers to it any more");
    }

    [Fact]
    public async Task KeptSnapshots_DoNotPileUp_OverManyCommits()
    {
        var id = await this.CreateAsync();
        for (var i = 0; i < 6; i++)
        {
            var draft = await this.OpenAsync(id, "writer");
            await this.PatchAsync(id, draft, $"Edit {i}");
            await this.CommitAsync(id, draft, "writer");
        }

        (await this.KeptBasesAsync(id)).Should().HaveCount(1, "only the snapshot just superseded is kept");
    }

    [Fact]
    public async Task ADraftStoredWithAnEmbeddedSnapshot_ByAnEarlierVersion_StillLoads_AndIsConvertedOnSave()
    {
        var id = await this.CreateAsync();
        var draft = await this.OpenAsync(id, "alice");
        await this.PatchAsync(id, draft, "Mine");

        // Rewrite the stored draft the way earlier versions stored it: with its base snapshot inside.
        await using (var session = fixture.Store.LightweightSession())
        {
            var snapshot = (await session.LoadAsync<AggregateSnapshotDocument>(id))!.Snapshot;
            var stored = (await session.LoadAsync<AggregateDraftDocument>(draft))!;
            stored.Snapshot = snapshot.Clone();
            session.Store(stored);
            await session.SaveChangesAsync();
        }

        var other = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, other, "Bob's");
        await this.CommitAsync(id, other, "bob");

        var loaded = await fixture.Service().GetDraftAsync(this.Deep, id, draft, default);
        loaded!.Data["Title"]!.GetValue<string>().Should().Be("Mine");
        loaded.IsStale.Should().BeTrue();

        await this.PatchAsync(id, draft, "Mine, again");
        (await this.LoadDraftAsync(draft)).Snapshot.Should().BeNull();
        (await fixture.Service().GetDraftAsync(this.Deep, id, draft, default))!.Data["Title"]!.GetValue<string>().Should().Be("Mine, again");
    }

    private async Task<Guid> CreateAsync() =>
        (await fixture.Service().CreateAsync(
            this.Deep,
            new CreateAggregateRequest("setup", new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["Title"] = JsonSerializer.SerializeToElement("Base") }),
            default)).Id;

    private async Task<Guid> OpenAsync(Guid id, string user) =>
        (await fixture.Service().OpenDraftAsync(this.Deep, id, new OpenDraftRequest(user), default))!.DraftId;

    private async Task<AggregateDraftResponse> PatchAsync(Guid id, Guid draft, string title) =>
        (await fixture.Service().PatchDraftAsync(this.Deep, id, draft, [new PatchOperationRequest("SET", "/Title", JsonSerializer.SerializeToElement(title))], default))!;

    private async Task CommitAsync(Guid id, Guid draft, string user)
    {
        var result = await fixture.Service().CommitDraftAsync(this.Deep, id, draft, new CommitDraftRequest(user), default);
        result!.Commit.Should().NotBeNull();
    }

    private async Task<AggregateDraftDocument> LoadDraftAsync(Guid draft)
    {
        await using var session = fixture.Store.QuerySession();
        return (await session.LoadAsync<AggregateDraftDocument>(draft))!;
    }

    private async Task<IReadOnlyList<Guid>> KeptBasesAsync(Guid aggregateId)
    {
        await using var session = fixture.Store.QuerySession();
        return await session.Query<AggregateBaseSnapshotDocument>().Where(document => document.AggregateId == aggregateId).Select(document => document.Id).ToListAsync();
    }
}
