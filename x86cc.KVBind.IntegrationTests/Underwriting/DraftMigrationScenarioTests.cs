using System.Text.Json;
using AwesomeAssertions;
using Marten;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.IntegrationTests.Fixtures;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.Persistence;
using x86cc.KVBind.Sample.Api.Underwriting;

namespace x86cc.KVBind.IntegrationTests.Underwriting;

// Users A and B branch off the same (schema v0) application. A commits; B leaves the draft open. The next
// morning's deployment brings migrations v1–v3. When B's draft is next read, the aggregate is migrated and
// B's draft is moved onto it: B's edits arrive in the new layout, A's committed change wins the conflict,
// and what B lost is listed on the draft.
[Collection(SampleApiCollection.Name)]
public sealed class DraftMigrationScenarioTests(SampleApiFixture fixture)
{
    private IAggregateModel Underwriting => fixture.Model(UnderwritingModel.Name);

    [Fact]
    public async Task ADraftOpenedBeforeADeployment_IsMovedOntoTheMigratedSnapshot_WhenItIsNextRead()
    {
        var (id, bob) = await this.ArrangeAsync();

        var draft = (await fixture.Service().GetDraftAsync(this.Underwriting, id, bob, default))!;

        draft.IsStale.Should().BeFalse();
        draft.BaseCommitId.Should().Be(draft.LatestCommitId);
        draft.Data["Basics"]!["ApplicantName"]!.GetValue<string>().Should().Be("Alice Holdings", "A's commit wins the conflict");
        draft.Data["Insured"]!["$type"]!.GetValue<string>().Should().Be("PERSON");
        draft.Data["Insured"]!["FullName"]!.GetValue<string>().Should().Be("Pat Bob", "B's edit arrives in the new layout");
        draft.Data.ContainsKey("LegacyScore").Should().BeFalse();

        draft.MigrationNotices.Select(notice => (notice.Kind, notice.Path)).Should().BeEquivalentTo(new[]
        {
            ("Replaced", "Basics/ApplicantName"),
            ("Removed", "LegacyScore"),
        });
        var replaced = draft.MigrationNotices.Single(notice => notice.Kind == "Replaced");
        replaced.DraftValue.Should().Be("Bob Holdings");
        replaced.CommittedValue.Should().Be("Alice Holdings");

        var reloaded = (await fixture.Service().GetDraftAsync(this.Underwriting, id, bob, default))!;
        reloaded.MigrationNotices.Should().HaveCount(2, "the notices are stored with the draft");
        reloaded.Data["Insured"]!["FullName"]!.GetValue<string>().Should().Be("Pat Bob", "migrating again finds the draft current");
    }

    [Fact]
    public async Task TheMigratedDraft_CanBeCommitted()
    {
        var (id, bob) = await this.ArrangeAsync();

        var result = await fixture.Service().CommitDraftAsync(this.Underwriting, id, bob, new CommitDraftRequest("bob"), default);

        result!.Commit.Should().NotBeNull();
        var snapshot = result.Commit.Snapshot;
        snapshot.SchemaVersion.Should().Be(UnderwritingDefinitionBuilder.SchemaVersion);
        snapshot.Data["Insured"]!["FullName"]!.GetValue<string>().Should().Be("Pat Bob");
        snapshot.Data["Basics"]!["ApplicantName"]!.GetValue<string>().Should().Be("Alice Holdings");
    }

    [Fact]
    public async Task ResettingTheMigratedDraft_ClearsItsNotices()
    {
        var (id, bob) = await this.ArrangeAsync();

        var reset = await fixture.Service().ResetDraftAsync(this.Underwriting, id, bob, default);

        reset!.MigrationNotices.Should().BeEmpty();
    }

    // A and B both change the locations; the total insured value (kept by a change reaction) differs on the
    // two sides. That conflict is no news to B: the total is recomputed after the move, over both changes.
    [Fact]
    public async Task ConflictsOnDerivedValues_AreRecomputed_NotReported()
    {
        var id = await this.CreateAsync([Add("/Locations", "a", "BUILDING"), Set("/Locations/a/Name", "A"), Set("/Locations/a/Value", 100)]);
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, bob, Set("/Locations/a/Value", 300));
        var alice = await this.OpenAsync(id, "alice");
        await this.PatchAsync(id, alice, Add("/Locations", "b", "VEHICLE"), Set("/Locations/b/Name", "B"), Set("/Locations/b/Value", 50));
        await this.CommitAsync(id, alice);
        await this.SimulateDeploymentAsync(id);

        var draft = (await fixture.Service().GetDraftAsync(this.Underwriting, id, bob, default))!;

        draft.IsStale.Should().BeFalse();
        var values = draft.Data["Locations"]!.AsArray()
            .ToDictionary(location => location!["$id"]!.GetValue<string>(), location => location!["Value"]!.GetValue<decimal>(), StringComparer.Ordinal);
        values.Should().BeEquivalentTo(new Dictionary<string, decimal>(StringComparer.Ordinal) { ["a"] = 300, ["b"] = 50 });
        draft.Data["TotalInsuredValue"]!.GetValue<decimal>().Should().Be(350, "recomputed over both users' locations");
        draft.Data["LocationCount"]!.GetValue<int>().Should().Be(2);
        draft.MigrationNotices.Should().BeEmpty("only derived values conflicted");
    }

    // B reorders the locations while A adds one: the committed order is kept, and B is told.
    [Fact]
    public async Task ALostReorder_IsKeptAsAReorderedNotice()
    {
        var id = await this.CreateAsync([
            Add("/Locations", "a", "BUILDING"), Set("/Locations/a/Name", "A"),
            Add("/Locations", "b", "BUILDING"), Set("/Locations/b/Name", "B")]);
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, bob, new PatchOperationRequest("MOVE", "/Locations/b", JsonSerializer.SerializeToElement(0)));
        var alice = await this.OpenAsync(id, "alice");
        await this.PatchAsync(id, alice, Add("/Locations", "c", "VEHICLE"), Set("/Locations/c/Name", "C"));
        await this.CommitAsync(id, alice);
        await this.SimulateDeploymentAsync(id);

        var draft = (await fixture.Service().GetDraftAsync(this.Underwriting, id, bob, default))!;

        draft.Data["Locations"]!.AsArray().Select(location => location!["$id"]!.GetValue<string>()).Should().Equal("a", "b", "c");
        var notice = draft.MigrationNotices.Should().ContainSingle().Subject;
        notice.Kind.Should().Be("Reordered");
        notice.Path.Should().Be("Locations");
        notice.DraftValue.Should().BeEquivalentTo(new[] { "b", "a" });
    }

    // A child aggregate reads its program terms from its parent. Both are migrated on load; the child's
    // draft is moved onto the child's migrated snapshot (its base comes from the kept copy) and never takes
    // over an inherited member.
    [Fact]
    public async Task AChildsDraft_IsMigrated_AndKeepsReadingInheritedMembersFromItsParent()
    {
        var parent = await this.CreateAsync([Set("/Program/ProgramName", "Contractors 2027")]);
        var child = (await fixture.Service().CreateAsync(
            this.Underwriting,
            new CreateAggregateRequest("alice", new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["Basics/ApplicationNumber"] = JsonSerializer.SerializeToElement("UW-3001") }, parent),
            default)).Id;
        var bob = await this.OpenAsync(child, "bob");
        await this.PatchAsync(child, bob, Set("/Basics/YearsInBusiness", 7));
        var baseCommit = (await fixture.Service().GetSnapshotAsync(this.Underwriting, child, default))!.LastCommitId;
        await this.SimulateDeploymentAsync(parent, child);

        var draft = (await fixture.Service().GetDraftAsync(this.Underwriting, child, bob, default))!;

        draft.IsStale.Should().BeFalse();
        draft.BaseCommitId.Should().NotBe(baseCommit!.Value, "the draft sits on the migration commit now");
        draft.Data["Basics"]!["YearsInBusiness"]!.GetValue<int>().Should().Be(7);
        draft.Data["Program"]!["ProgramName"]!.GetValue<string>().Should().Be("Contractors 2027");
        draft.InheritedPaths.Should().Contain("Program");
        draft.MigrationNotices.Should().BeEmpty();

        await using var session = fixture.Store.QuerySession();
        var stored = (await session.LoadAsync<AggregateDraftDocument>(bob))!;
        stored.Changes.Keys.Should().NotContain(key => key.StartsWith("Program"));
    }

    private static PatchOperationRequest Set(string path, object value) => new("SET", path, JsonSerializer.SerializeToElement(value));

    private static PatchOperationRequest Add(string path, string itemId, string typeToken) =>
        new("ADD", path, JsonSerializer.SerializeToElement(new { itemId, typeToken }));

    // Stores, as an older release would have: a v0 application, B's draft on it (old layout, base embedded),
    // then A's commit on top. Nothing has been migrated yet.
    private async Task<(Guid Id, Guid BobDraft)> ArrangeAsync()
    {
        var id = Guid.NewGuid();
        var original = UnderwritingModel.LegacySnapshot("importer");
        original.LastCommitId = Guid.NewGuid();
        original.LastCommitTimestamp = DateTimeOffset.UtcNow.AddDays(-1);

        var overlay = KVOverlay.Create(original.Clone(), "bob");
        overlay.Set("Basics/InsuredName", new KVValue<string>("Bob Holdings"));
        overlay.Set("Insured/Name", new KVValue<string>("Pat Bob"));
        overlay.Set("LegacyScore", new KVValue<int>(7));
        var bobDraft = AggregateDraftDocument.Create(this.Underwriting.Name, id, "bob", overlay);
        bobDraft.Snapshot = original.Clone();

        var aliceOverlay = KVOverlay.Create(original.Clone(), "alice");
        aliceOverlay.Set("Basics/InsuredName", new KVValue<string>("Alice Holdings"));
        var aliceCommit = aliceOverlay.ToCommit(DateTimeOffset.UtcNow.AddHours(-12));
        var head = original.Clone();
        head.Apply(aliceCommit);

        await using var session = fixture.Store.LightweightSession();
        session.Store(new AggregateSnapshotDocument { Id = id, Model = this.Underwriting.Name, Snapshot = head });
        session.Store(new AggregateChangeSetDocument { Id = aliceCommit.CommitId, Model = this.Underwriting.Name, AggregateId = id, Commit = aliceCommit });
        session.Store(bobDraft);
        await session.SaveChangesAsync();
        return (id, bobDraft.Id);
    }

    // The data was written by this build; pretend the newest migration came with a later deployment, so it
    // is still pending for these aggregates and for the snapshots kept as draft bases. Re-running the newest
    // migration over current data changes nothing but the schema version.
    private async Task SimulateDeploymentAsync(params Guid[] ids)
    {
        await using var session = fixture.Store.LightweightSession();
        foreach (var id in ids)
        {
            var document = (await session.LoadAsync<AggregateSnapshotDocument>(id))!;
            document.Snapshot.SchemaVersion = UnderwritingDefinitionBuilder.SchemaVersion - 1;
            session.Store(document);

            var kept = await session.Query<AggregateBaseSnapshotDocument>().Where(base_ => base_.AggregateId == id).ToListAsync();
            foreach (var keptBase in kept)
            {
                keptBase.Snapshot.SchemaVersion = UnderwritingDefinitionBuilder.SchemaVersion - 1;
                session.Store(keptBase);
            }
        }

        await session.SaveChangesAsync();
    }

    private async Task<Guid> CreateAsync(IReadOnlyList<PatchOperationRequest> patches)
    {
        var number = $"UW-{Random.Shared.Next(10_000, 99_999)}";
        var id = (await fixture.Service().CreateAsync(
            this.Underwriting,
            new CreateAggregateRequest("setup", new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["Basics/ApplicationNumber"] = JsonSerializer.SerializeToElement(number) }),
            default)).Id;
        var draft = await this.OpenAsync(id, "setup");
        await this.PatchAsync(id, draft, [.. patches]);
        await this.CommitAsync(id, draft);
        return id;
    }

    private async Task<Guid> OpenAsync(Guid id, string user) =>
        (await fixture.Service().OpenDraftAsync(this.Underwriting, id, new OpenDraftRequest(user), default))!.DraftId;

    private Task PatchAsync(Guid id, Guid draft, params PatchOperationRequest[] patches) =>
        fixture.Service().PatchDraftAsync(this.Underwriting, id, draft, patches, default);

    private async Task CommitAsync(Guid id, Guid draft)
    {
        var result = await fixture.Service().CommitDraftAsync(this.Underwriting, id, draft, new CommitDraftRequest("setup"), default);
        result!.Commit.Should().NotBeNull();
    }
}
