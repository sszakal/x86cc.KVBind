using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using JasperFx;
using x86cc.KVBind.IntegrationTests.Fixtures;
using x86cc.KVBind.Sample.Api.Aggregates;
using x86cc.KVBind.Sample.Api.Underwriting;

namespace x86cc.KVBind.IntegrationTests.Underwriting;

// API-level scenarios against Postgres, through the generic AggregateService exactly as the endpoints use
// it: 1l drafts, rebase and concurrency · 1m migrations · 1n inheritance · persistence of every value kind.
[Collection(SampleApiCollection.Name)]
public sealed class ApiScenarioTests(SampleApiFixture fixture)
{
    private const string Model = UnderwritingModel.Name;

    private IAggregateModel Underwriting => fixture.Model(Model);

    // ── Persistence round trip ──────────────────────────────────────────────────

    [Fact]
    public async Task EveryValueKind_SurvivesStorageInPostgres()
    {
        var (id, draft) = await this.CreateAndOpenAsync("alice");
        var broker = Guid.NewGuid();

        await this.PatchAsync(
            id,
            draft,
            Set("/Basics/YearsInBusiness", 12),
            Set("/Basics/AnnualRevenue", 2_500_000.5m),
            Set("/Basics/IsRenewal", true),
            Set("/Basics/EffectiveDate", "2027-03-01"),
            Set("/Basics/ReceivedAt", "2026-11-14T09:30:00Z"),
            Set("/Basics/PreferredCallTime", "14:30:00"),
            Set("/Basics/ReviewSla", "3.00:00:00"),
            Set("/Basics/BrokerReference", broker.ToString()),
            Set("/Basics/Channel", "Online"),
            Set("/Basics/Coverages", "Fire, Flood"),
            Set("/LineOfBusiness", "Cyber"),
            Set("/Classification/Territory", 3),
            Set("/Classification/Exclusions", JsonDocument.Parse("[\"war\",\"cyber\"]").RootElement));
        await this.CommitAsync(id, draft, "alice");

        var data = (await fixture.Service().GetSnapshotAsync(this.Underwriting, id, default))!.Data;
        var basics = data["Basics"]!.AsObject();
        basics["YearsInBusiness"]!.GetValue<int>().Should().Be(12);
        basics["AnnualRevenue"]!.GetValue<decimal>().Should().Be(2_500_000.5m);
        basics["IsRenewal"]!.GetValue<bool>().Should().BeTrue();
        basics["EffectiveDate"]!.GetValue<string>().Should().Be("2027-03-01");
        basics["PreferredCallTime"]!.GetValue<string>().Should().Be("14:30:00");
        basics["ReviewSla"]!.GetValue<string>().Should().Be("3.00:00:00");
        basics["BrokerReference"]!.GetValue<string>().Should().Be(broker.ToString());
        basics["Channel"]!.GetValue<string>().Should().Be("Online");
        basics["Coverages"]!.GetValue<string>().Should().Be("Fire, Flood");
        data["LineOfBusiness"]!.GetValue<string>().Should().Be("Cyber");
        data["Classification"]!["Territory"]!.GetValue<int>().Should().Be(3);
        data["Classification"]!["Exclusions"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("war", "cyber");
    }

    [Fact]
    public async Task Create_AppliesDefaultsAndSeeds_AndReportsInvalidCreateValues()
    {
        var created = await fixture.Service().CreateAsync(this.Underwriting, new CreateAggregateRequest("alice", Values("Basics/ApplicationNumber", "UW-2001")), default);

        created.Data["Status"]!.GetValue<string>().Should().Be("draft");
        created.Data["Questions"]!.AsArray().Should().HaveCount(3);
        created.Data["Insured"]!["$type"]!.GetValue<string>().Should().Be("COMPANY");

        var invalid = () => fixture.Service().CreateAsync(this.Underwriting, new CreateAggregateRequest("alice", Values("Basics/ApplicationNumber", "nope")), default);
        await invalid.Should().ThrowAsync<x86cc.KVBind.Core.KVChangeSetValidationException>();
    }

    // ── 1k + 1h through the API: events are stored with the commit ───────────────

    [Fact]
    public async Task OperationEvents_ArePendingOnTheDraft_AndPublishedWithTheCommit()
    {
        var (id, draft) = await this.CreateAndOpenAsync("alice");
        await this.PatchAsync(id, draft, Op("DECLINE", "/", "Outside appetite"));

        var pending = await fixture.Service().GetDraftAsync(this.Underwriting, id, draft, default);
        pending!.PendingEvents.Should().ContainSingle().Which.Type.Should().Be(nameof(ApplicationDeclined));

        var commit = await this.CommitAsync(id, draft, "alice");
        commit.Events.Should().ContainSingle().Which.Payload.Should().Contain("Outside appetite");

        var history = (await fixture.Service().ListChangeSetsAsync(this.Underwriting, id, 0, 100, default)).Items;
        history[0].Events.Should().ContainSingle().Which.Type.Should().Be(nameof(ApplicationDeclined));
    }

    [Fact]
    public async Task ResetDraft_DropsItsPendingEvents()
    {
        var (id, draft) = await this.CreateAndOpenAsync("alice");
        await this.PatchAsync(id, draft, Op("DECLINE", "/", "x"));

        var reset = await fixture.Service().ResetDraftAsync(this.Underwriting, id, draft, default);

        reset!.PendingEvents.Should().BeEmpty();
        reset.Data["Status"]!.GetValue<string>().Should().Be("draft");
    }

    [Fact]
    public async Task InvalidDraft_IsRejectedOnCommit_WithItsErrors()
    {
        var (id, draft) = await this.CreateAndOpenAsync("alice");
        await this.PatchAsync(id, draft, Op("SUBMIT", "/"));

        var result = await fixture.Service().CommitDraftAsync(this.Underwriting, id, draft, new CommitDraftRequest("alice"), default);

        result!.Invalid.Should().NotBeNull();
        result.Invalid.Profile.Should().Be("submit");
        result.Invalid.Errors.Should().Contain(e => e.Path == "Basics/ApplicantName" && e.Code == "required");
    }

    // ── 1l: drafts, rebase and concurrency ───────────────────────────────────────

    [Fact]
    public async Task StaleDraft_CannotCommit_UntilRebased_AndNonConflictingChangesMerge()
    {
        var id = await this.CreateAsync();
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");

        await this.PatchAsync(id, alice, Set("/Basics/ApplicantName", "Acme"));
        await this.PatchAsync(id, bob, Set("/Contact/Phone", "+44 20 7946 0000"));
        await this.CommitAsync(id, alice, "alice");

        var stale = await fixture.Service().CommitDraftAsync(this.Underwriting, id, bob, new CommitDraftRequest("bob"), default);
        stale!.StaleDraft.Should().NotBeNull();

        var rebase = await fixture.Service().BeginRebaseAsync(this.Underwriting, id, bob, default);
        rebase!.Outcome.Should().Be("CanAutomerge");
        rebase.Conflicts.Should().OnlyContain(c => c.IsIncoming && !c.RequiresResolution);
        await fixture.Service().FinishRebaseAsync(this.Underwriting, id, bob, default);
        await this.CommitAsync(id, bob, "bob");

        var data = (await fixture.Service().GetSnapshotAsync(this.Underwriting, id, default))!.Data;
        data["Basics"]!["ApplicantName"]!.GetValue<string>().Should().Be("Acme");
        data["Contact"]!["Phone"]!.GetValue<string>().Should().Be("+44 20 7946 0000");
    }

    [Fact]
    public async Task ValueConflict_IsResolvedWithTheirs_Ours_OrCustom()
    {
        var id = await this.CreateAsync();
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, alice, Set("/Basics/ApplicantName", "Alice's name"), Set("/Contact/Email", "a@x.io"), Set("/Premises/Floors", 2));
        await this.PatchAsync(id, bob, Set("/Basics/ApplicantName", "Bob's name"), Set("/Contact/Email", "b@x.io"), Set("/Premises/Floors", 3));
        await this.CommitAsync(id, alice, "alice");

        var rebase = await fixture.Service().BeginRebaseAsync(this.Underwriting, id, bob, default);
        rebase!.Outcome.Should().Be("HasUnresolvedConflicts");
        rebase.Conflicts.Where(c => c.RequiresResolution).Select(c => c.Path)
            .Should().BeEquivalentTo("Basics/ApplicantName", "Contact/Email", "Premises/Floors");

        var finishEarly = () => fixture.Service().FinishRebaseAsync(this.Underwriting, id, bob, default);
        await finishEarly.Should().ThrowAsync<Sample.Api.ApiRequestException>();

        await this.ResolveAsync(id, bob, "Basics/ApplicantName", "Theirs");
        await this.ResolveAsync(id, bob, "Contact/Email", "Ours");
        await this.ResolveAsync(id, bob, "Premises/Floors", "Custom", JsonDocument.Parse("7").RootElement);
        await fixture.Service().FinishRebaseAsync(this.Underwriting, id, bob, default);
        await this.CommitAsync(id, bob, "bob");

        var data = (await fixture.Service().GetSnapshotAsync(this.Underwriting, id, default))!.Data;
        data["Basics"]!["ApplicantName"]!.GetValue<string>().Should().Be("Alice's name");
        data["Contact"]!["Email"]!.GetValue<string>().Should().Be("b@x.io");
        data["Premises"]!["Floors"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public async Task DeleteEditConflict_And_StructuralConflict_AreReported()
    {
        var id = await this.CreateAsync(patches:
        [
            Add("/Locations", "hq", "BUILDING"),
            Set("/Locations/hq/Name", "HQ"),
        ]);
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");

        // Alice removes the location and switches the insured to a person; Bob edits both.
        await this.PatchAsync(id, alice, Remove("/Locations/hq"), Init("/Insured", "PERSON"), Set("/Insured/FullName", "Ann"));
        await this.PatchAsync(id, bob, Set("/Locations/hq/Value", 500), Set("/Insured/CompanyName", "Bobco"));
        await this.CommitAsync(id, alice, "alice");

        var rebase = await fixture.Service().BeginRebaseAsync(this.Underwriting, id, bob, default);

        var conflicts = rebase!.Conflicts.Where(c => c.RequiresResolution).ToArray();
        conflicts.Should().Contain(c => c.Kind == "DeleteEdit" && c.Path.StartsWith("Locations/hq"));
        conflicts.Should().Contain(c => c.Kind == "Structural" && c.Path.StartsWith("Insured"));
    }

    [Fact]
    public async Task CancelRebase_KeepsTheDraftOnItsOldBase()
    {
        var id = await this.CreateAsync();
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, alice, Set("/Basics/ApplicantName", "A"));
        await this.PatchAsync(id, bob, Set("/Basics/ApplicantName", "B"));
        await this.CommitAsync(id, alice, "alice");
        await fixture.Service().BeginRebaseAsync(this.Underwriting, id, bob, default);

        var cancelled = await fixture.Service().CancelRebaseAsync(this.Underwriting, id, bob, default);

        cancelled!.IsRebasing.Should().BeFalse();
        cancelled.IsStale.Should().BeTrue();
        cancelled.Data["Basics"]!["ApplicantName"]!.GetValue<string>().Should().Be("B");
    }

    [Fact]
    public async Task RebaseFinish_RecomputesDerivedValues()
    {
        var id = await this.CreateAsync(patches: [Add("/Locations", "a", "BUILDING"), Set("/Locations/a/Name", "A"), Set("/Locations/a/Value", 100)]);
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, alice, Set("/Locations/a/Value", 300));
        await this.PatchAsync(id, bob, Add("/Locations", "b", "VEHICLE"), Set("/Locations/b/Name", "B"), Set("/Locations/b/Value", 50));
        await this.CommitAsync(id, alice, "alice");

        await fixture.Service().BeginRebaseAsync(this.Underwriting, id, bob, default);
        var finished = await fixture.Service().FinishRebaseAsync(this.Underwriting, id, bob, default);

        finished!.Data["TotalInsuredValue"]!.GetValue<decimal>().Should().Be(350m, "Alice's 300 plus Bob's 50, recomputed after the merge");
        finished.Data["LastRecalculation"]!.GetValue<string>().Should().Be("Replaced");
    }

    [Fact]
    public async Task Rebase_ShowsALostReorder_AsAFixedConflict_ThatDoesNotBlockFinishing()
    {
        var id = await this.CreateAsync(patches: [Add("/Locations", "a", "BUILDING"), Set("/Locations/a/Name", "A"), Add("/Locations", "b", "BUILDING"), Set("/Locations/b/Name", "B")]);
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, bob, Op("MOVE", "/Locations/b", 0));
        await this.PatchAsync(id, alice, Add("/Locations", "c", "VEHICLE"), Set("/Locations/c/Name", "C"));
        await this.CommitAsync(id, alice, "alice");

        var rebase = await fixture.Service().BeginRebaseAsync(this.Underwriting, id, bob, default);

        rebase!.Outcome.Should().Be(nameof(KVBind.Core.Model.KVRebaseOutcome.CanAutomerge));
        var order = rebase.Conflicts.Should().ContainSingle(c => c.Kind == "Order").Subject;
        order.Path.Should().Be("Locations");
        order.IsFixed.Should().BeTrue();
        order.RequiresResolution.Should().BeFalse();
        order.Resolution.Should().Be("Theirs");
        var choose = () => fixture.Service().ResolveRebaseConflictAsync(this.Underwriting, id, bob, new ResolveRebaseConflictRequest("Locations", "Ours"), default);
        await choose.Should().ThrowAsync<Exception>();

        var finished = await fixture.Service().FinishRebaseAsync(this.Underwriting, id, bob, default);
        finished!.Data["Locations"]!.AsArray().Select(location => location!["$id"]!.GetValue<string>()).Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task ConcurrentSaves_OfTheSameDraft_LoseNoUpdate()
    {
        var (id, draft) = await this.CreateAndOpenAsync("alice");
        var first = fixture.Service();
        var second = fixture.Service();

        // Both requests load the draft, then both save: the second must fail instead of overwriting the first.
        var one = first.PatchDraftAsync(this.Underwriting, id, draft, [Set("/Basics/ApplicantName", "One")], default);
        var two = second.PatchDraftAsync(this.Underwriting, id, draft, [Set("/Contact/Phone", "+1 555 0100")], default);
        var results = await Task.WhenAll(Wrap(one), Wrap(two));

        var reloaded = await fixture.Service().GetDraftAsync(this.Underwriting, id, draft, default);
        var appliedBoth = reloaded!.Data["Basics"]!["ApplicantName"] is not null && reloaded.Data["Contact"]!["Phone"] is not null;
        (appliedBoth || results.Any(r => r is ConcurrencyException)).Should().BeTrue("a concurrent save either serializes or is rejected, never silently lost");
    }

    [Fact]
    public async Task ConcurrentCommits_OfTwoDrafts_CannotForkTheHistory()
    {
        var id = await this.CreateAsync();
        var alice = await this.OpenAsync(id, "alice");
        var bob = await this.OpenAsync(id, "bob");
        await this.PatchAsync(id, alice, Set("/Basics/ApplicantName", "A"));
        await this.PatchAsync(id, bob, Set("/Contact/Phone", "+1 555 0100"));

        var results = await Task.WhenAll(
            Wrap(fixture.Service().CommitDraftAsync(this.Underwriting, id, alice, new CommitDraftRequest("alice"), default)),
            Wrap(fixture.Service().CommitDraftAsync(this.Underwriting, id, bob, new CommitDraftRequest("bob"), default)));

        var history = (await fixture.Service().ListChangeSetsAsync(this.Underwriting, id, 0, 100, default)).Items;
        history.Select(c => c.PreviousCommitId).Should().OnlyHaveUniqueItems("every commit has its own parent: a single chain");
        results.Count(r => r is CommitResult { Commit: not null }).Should().BeGreaterThanOrEqualTo(1);
    }

    // ── 1m: migrations ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LegacyAggregate_IsMigratedOnFirstRead_WithMigrationCommitsInItsHistory()
    {
        var legacy = await fixture.Service().CreateLegacyAsync(this.Underwriting, "importer", default);
        legacy.SchemaVersion.Should().Be(0);

        var snapshot = await fixture.Service().GetSnapshotAsync(this.Underwriting, legacy.Id, default);

        snapshot!.SchemaVersion.Should().Be(UnderwritingDefinitionBuilder.SchemaVersion);
        var data = snapshot.Data;
        data["Basics"]!["ApplicantName"]!.GetValue<string>().Should().StartWith("Legacy Holdings", "v1 renamed InsuredName");
        data["Basics"]!["Channel"]!.GetValue<string>().Should().Be("Broker", "v1 backfilled the channel");
        data["Insured"]!["$type"]!.GetValue<string>().Should().Be("PERSON", "v2 replaced the INDIVIDUAL type");
        data["Insured"]!["FullName"]!.GetValue<string>().Should().StartWith("Pat Legacy", "v2 renamed Name to FullName");
        data["Basics"]!["ApplicationNumber"]!.GetValue<string>().Should().StartWith("UW-", "v3 built it from LegacyReference");
        data.ContainsKey("LegacyScore").Should().BeFalse();

        var history = (await fixture.Service().ListChangeSetsAsync(this.Underwriting, legacy.Id, 0, 100, default)).Items;
        history.Select(c => c.MigrationToVersion).Should().BeEquivalentTo(new int?[] { 1, 2, 3 });
    }

    [Fact]
    public async Task BatchMigration_BringsEveryLegacyAggregateUpToDate()
    {
        var first = await fixture.Service().CreateLegacyAsync(this.Underwriting, "importer", default);
        var second = await fixture.Service().CreateLegacyAsync(this.Underwriting, "importer", default);

        var result = await fixture.Service().MigrateAllAsync(this.Underwriting, default);

        result.Migrated.Should().BeGreaterThanOrEqualTo(2);
        var list = await fixture.Service().ListAsync(this.Underwriting, default);
        list.Where(a => a.Id == first.Id || a.Id == second.Id).Should().OnlyContain(a => a.SchemaVersion == UnderwritingDefinitionBuilder.SchemaVersion);
        (await fixture.Service().MigrateAllAsync(this.Underwriting, default)).Migrated.Should().Be(0, "a second run finds nothing behind");
    }

    [Fact]
    public async Task MigratedAggregate_CanBeEditedAndCommitted()
    {
        var legacy = await fixture.Service().CreateLegacyAsync(this.Underwriting, "importer", default);
        var draft = await this.OpenAsync(legacy.Id, "alice");

        await this.PatchAsync(legacy.Id, draft, Set("/Insured/DateOfBirth", "1980-05-05"));
        var commit = await this.CommitAsync(legacy.Id, draft, "alice");

        commit.Snapshot.Data["Insured"]!["DateOfBirth"]!.GetValue<string>().Should().Be("1980-05-05");
    }

    // ── 1n: inheritance ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Child_ReadsInheritedProgramTerms_FromItsParent_AndCannotChangeThem()
    {
        var parent = await this.CreateAsync(patches: [Set("/Program/ProgramName", "Contractors 2027"), Set("/Program/CommissionPercent", 12.5m)]);

        var child = await fixture.Service().CreateAsync(this.Underwriting, new CreateAggregateRequest("alice", Values("Basics/ApplicationNumber", "UW-3001"), parent), default);

        child.ParentId.Should().Be(parent);
        child.InheritedPaths.Should().Contain("Program");
        child.Data["Program"]!["ProgramName"]!.GetValue<string>().Should().Be("Contractors 2027");

        var draft = await this.OpenAsync(child.Id, "alice");
        var write = () => this.PatchAsync(child.Id, draft, Set("/Program/ProgramName", "Mine"));
        await write.Should().ThrowAsync<Sample.Api.ApiRequestException>();
    }

    [Fact]
    public async Task ParentUpdates_FlowThroughToItsChildren()
    {
        var parent = await this.CreateAsync(patches: [Set("/Program/Carrier", "Old Carrier")]);
        var child = await fixture.Service().CreateAsync(this.Underwriting, new CreateAggregateRequest("alice", Values("Basics/ApplicationNumber", "UW-3002"), parent), default);

        var parentDraft = await this.OpenAsync(parent, "admin");
        await this.PatchAsync(parent, parentDraft, Set("/Program/Carrier", "New Carrier"));
        await this.CommitAsync(parent, parentDraft, "admin");

        var reloaded = await fixture.Service().GetSnapshotAsync(this.Underwriting, child.Id, default);
        reloaded!.Data["Program"]!["Carrier"]!.GetValue<string>().Should().Be("New Carrier");
    }

    [Fact]
    public async Task Claims_DoNotSupportChildren()
    {
        var claims = fixture.Model("claims");
        var claim = await fixture.Service().CreateAsync(claims, new CreateAggregateRequest("alice", Values("ClaimNumber", "C-1")), default);

        var child = () => fixture.Service().CreateAsync(claims, new CreateAggregateRequest("alice", null, claim.Id), default);

        await child.Should().ThrowAsync<Sample.Api.ApiRequestException>();
    }

    // ── Helpers: the service calls the endpoints make ───────────────────────────

    private static async Task<object?> Wrap<T>(Task<T> task)
    {
        try
        {
            return await task;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Values(string path, string value) =>
        new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [path] = JsonSerializer.SerializeToElement(value) };

    private static PatchOperationRequest Set(string path, object value) =>
        new("SET", path, value is JsonElement json ? json : JsonSerializer.SerializeToElement(value));

    private static PatchOperationRequest Op(string operation, string path, object? value = null) =>
        new(operation, path, value is null ? null : JsonSerializer.SerializeToElement(value));

    private static PatchOperationRequest Add(string path, string itemId, string typeToken) =>
        new("ADD", path, JsonSerializer.SerializeToElement(new { itemId, typeToken }));

    private static PatchOperationRequest Remove(string path) => new("REMOVE", path);

    private static PatchOperationRequest Init(string path, string token) => new("INIT", path, JsonSerializer.SerializeToElement(token));

    private async Task<Guid> CreateAsync(IReadOnlyList<PatchOperationRequest>? patches = null)
    {
        var number = $"UW-{Random.Shared.Next(10_000, 99_999)}";
        var id = (await fixture.Service().CreateAsync(this.Underwriting, new CreateAggregateRequest("setup", Values("Basics/ApplicationNumber", number)), default)).Id;
        if (patches is { Count: > 0 })
        {
            var draft = await this.OpenAsync(id, "setup");
            await this.PatchAsync(id, draft, [.. patches]);
            await this.CommitAsync(id, draft, "setup");
        }

        return id;
    }

    private async Task<(Guid Id, Guid Draft)> CreateAndOpenAsync(string user)
    {
        var id = await this.CreateAsync();
        return (id, await this.OpenAsync(id, user));
    }

    private async Task<Guid> OpenAsync(Guid id, string user) =>
        (await fixture.Service().OpenDraftAsync(this.Underwriting, id, new OpenDraftRequest(user), default))!.DraftId;

    private async Task<AggregateDraftResponse> PatchAsync(Guid id, Guid draft, params PatchOperationRequest[] operations) =>
        (await fixture.Service().PatchDraftAsync(this.Underwriting, id, draft, operations, default))!;

    private async Task<CommitResponse> CommitAsync(Guid id, Guid draft, string user)
    {
        var result = await fixture.Service().CommitDraftAsync(this.Underwriting, id, draft, new CommitDraftRequest(user), default);
        result!.Invalid.Should().BeNull(result.Invalid is null ? string.Empty : string.Join("; ", result.Invalid.Errors.Select(e => $"{e.Path} {e.Code}")));
        result.StaleDraft.Should().BeNull();
        return result.Commit!;
    }

    private Task ResolveAsync(Guid id, Guid draft, string path, string resolution, JsonElement? value = null) =>
        fixture.Service().ResolveRebaseConflictAsync(this.Underwriting, id, draft, new ResolveRebaseConflictRequest(path, resolution, value), default);
}
