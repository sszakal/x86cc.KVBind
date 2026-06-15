using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Core.Model;
using x86cc.KVBind.IntegrationTests.Fixtures;
using x86cc.KVBind.IntegrationTests.Models;
using x86cc.KVBind.IntegrationTests.Persistence;

namespace x86cc.KVBind.IntegrationTests;

public sealed class MartenSerializationIntegrationTests : PostgresMartenTestBase
{
    private static readonly KVNodeDefinition Definition = IntegrationGraphDefinition.Create();
    private static readonly Guid AggregateId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ExternalId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid BaseOrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BaseLineId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BaseAdjustmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DraftOrderId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DraftLineId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid DraftAdjustmentId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task SnapshotRoundTrip_WithFullGraph_PreservesAllValues()
    {
        var snapshot = CreateFullSnapshot();

        await using (var session = Store.LightweightSession())
        {
            session.Store(new IntegrationSnapshotDocument { Id = AggregateId, Snapshot = snapshot });
            await session.SaveChangesAsync();
        }

        IntegrationSnapshotDocument? reloaded;
        await using (var session = Store.QuerySession())
        {
            reloaded = await session.LoadAsync<IntegrationSnapshotDocument>(AggregateId);
        }

        reloaded.Should().NotBeNull();
        reloaded!.Snapshot.Data["DateLookingText"].Should().BeOfType<KVValue<string>>();
        reloaded.Snapshot.Data["DateTimeValue"].Should().BeOfType<KVValue<DateTime>>();
        reloaded.Snapshot.Data["Price"].Should().BeOfType<KVValue<decimal>>();
        reloaded.Snapshot.Data["Tags"].Should().BeOfType<KVValue<string[]>>();
        AssertStoredString(reloaded.Snapshot.Data["SmartStatus"], "in_review");
        AssertStoredString(reloaded.Snapshot.Data["CompensationType"], "assistant_hourly");
        AssertStoredJsonArray(reloaded.Snapshot.Data["Contact/StatusHistory"], ["new", "in_review"]);
        AssertStoredJsonArray(reloaded.Snapshot.Data["Contact/StatusList"], ["in_review", "approved"]);
        AssertStoredJsonArray(reloaded.Snapshot.Data["Contact/CompensationHistory"], ["assistant_hourly", "manager_flat"]);
        AssertStoredJsonArray(reloaded.Snapshot.Data["Contact/CompensationList"], ["manager_flat", "assistant_hourly"]);
        AssertStoredJsonArray(reloaded.Snapshot.Data[$"Orders/{BaseOrderId:D}/Lines/{BaseLineId:D}/Adjustments/{BaseAdjustmentId:D}/StatusHistory"], ["new", "approved"]);
        AssertStoredJsonArray(reloaded.Snapshot.Data[$"Orders/{BaseOrderId:D}/Lines/{BaseLineId:D}/Adjustments/{BaseAdjustmentId:D}/StatusList"], ["in_review", "approved"]);
        AssertStoredJsonArray(reloaded.Snapshot.Data[$"Orders/{BaseOrderId:D}/Lines/{BaseLineId:D}/Adjustments/{BaseAdjustmentId:D}/CompensationHistory"], ["assistant_hourly", "manager_flat"]);
        AssertStoredJsonArray(reloaded.Snapshot.Data[$"Orders/{BaseOrderId:D}/Lines/{BaseLineId:D}/Adjustments/{BaseAdjustmentId:D}/CompensationList"], ["manager_flat", "assistant_hourly"]);
        reloaded.Snapshot.Data.Should().ContainKey("Contact/$type");
        reloaded.Snapshot.Data.Should().ContainKey($"Orders/{BaseOrderId:D}/Lines/{BaseLineId:D}/Adjustments/{BaseAdjustmentId:D}/$type");

        var root = Bind(KVOverlay.Create(reloaded.Snapshot, "reader"));
        AssertFullGraph(root);
    }

    [Fact]
    public async Task OverlayRoundTrip_WithEditsAndDeepCollections_PreservesDraftState()
    {
        var snapshot = await PersistSnapshotAsync(CreateFullSnapshot());
        var overlay = KVOverlay.Create(snapshot.Clone(), "adjuster-a");
        var root = Bind(overlay);
        ApplyDraftEdits(root);

        var overlayDocument = IntegrationOverlayDocument.Create(AggregateId, "adjuster-a", overlay);
        await using (var session = Store.LightweightSession())
        {
            session.Store(overlayDocument);
            await session.SaveChangesAsync();
        }

        IntegrationOverlayDocument? reloaded;
        await using (var session = Store.QuerySession())
        {
            reloaded = await session.LoadAsync<IntegrationOverlayDocument>(overlayDocument.Id);
        }

        reloaded.Should().NotBeNull();
        reloaded!.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/Amount"].Should().BeOfType<KVValue<decimal>>();
        AssertStoredString(reloaded.Changes["SmartStatus"], "approved");
        AssertStoredString(reloaded.Changes["CompensationType"], "manager_flat");
        AssertStoredJsonArray(reloaded.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/StatusHistory"], ["approved", "new"]);
        AssertStoredJsonArray(reloaded.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/StatusList"], ["new", "in_review"]);
        AssertStoredJsonArray(reloaded.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/CompensationHistory"], ["manager_flat", "assistant_hourly"]);
        AssertStoredJsonArray(reloaded.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/CompensationList"], ["assistant_hourly", "manager_flat"]);
        reloaded.Changes.Should().ContainKey("Contact/$type");
        reloaded.Changes.Should().ContainKey($"Orders/{BaseOrderId:D}").WhoseValue.Should().Be(KVValue.Tombstone);

        var draftRoot = Bind(reloaded.ToOverlay());
        AssertDraftGraph(draftRoot);
        draftRoot.GetAllChanges().Changes.Should().Contain(change => change.Path == $"Orders/{BaseOrderId:D}" && change.ChangeType == KVChangeDeltaType.Removed);
        draftRoot.GetAllChanges().Changes.Should().Contain(change => change.Path == $"Orders/{DraftOrderId:D}" && change.ChangeType == KVChangeDeltaType.Added);
    }

    [Fact]
    public async Task CommitRoundTrip_AfterOverlayReload_PreservesCommitAndFinalSnapshot()
    {
        var snapshot = await PersistSnapshotAsync(CreateFullSnapshot());
        var overlay = KVOverlay.Create(snapshot.Clone(), "adjuster-a");
        var root = Bind(overlay);
        ApplyDraftEdits(root);

        var overlayDocument = IntegrationOverlayDocument.Create(AggregateId, "adjuster-a", overlay);
        await using (var session = Store.LightweightSession())
        {
            session.Store(overlayDocument);
            await session.SaveChangesAsync();
        }

        IntegrationOverlayDocument reloadedOverlay;
        await using (var session = Store.QuerySession())
        {
            reloadedOverlay = (await session.LoadAsync<IntegrationOverlayDocument>(overlayDocument.Id))!;
        }

        var commitRoot = Bind(reloadedOverlay.ToOverlay());
        var commit = commitRoot.CreateCommit(DateTimeOffset.Parse("2026-06-03T08:15:00+00:00"));
        commit.User = "adjuster-a";
        snapshot.Apply(commit);

        await using (var session = Store.LightweightSession())
        {
            session.Store(new IntegrationCommitDocument { Id = commit.CommitId, AggregateId = AggregateId, Commit = commit });
            session.Store(new IntegrationSnapshotDocument { Id = AggregateId, Snapshot = snapshot });
            await session.SaveChangesAsync();
        }

        IntegrationCommitDocument? reloadedCommit;
        IntegrationSnapshotDocument? reloadedSnapshot;
        await using (var session = Store.QuerySession())
        {
            reloadedCommit = await session.LoadAsync<IntegrationCommitDocument>(commit.CommitId);
            reloadedSnapshot = await session.LoadAsync<IntegrationSnapshotDocument>(AggregateId);
        }

        reloadedCommit.Should().NotBeNull();
        reloadedCommit!.Commit.Changes["DateTimeValue"].Should().BeOfType<KVValue<DateTime>>();
        reloadedCommit.Commit.Changes["Details"].Should().BeOfType<KVValue<ComplexDetails>>();
        AssertStoredString(reloadedCommit.Commit.Changes["SmartStatus"], "approved");
        AssertStoredString(reloadedCommit.Commit.Changes["CompensationType"], "manager_flat");
        AssertStoredJsonArray(reloadedCommit.Commit.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/StatusHistory"], ["approved", "new"]);
        AssertStoredJsonArray(reloadedCommit.Commit.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/StatusList"], ["new", "in_review"]);
        AssertStoredJsonArray(reloadedCommit.Commit.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/CompensationHistory"], ["manager_flat", "assistant_hourly"]);
        AssertStoredJsonArray(reloadedCommit.Commit.Changes[$"Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/CompensationList"], ["assistant_hourly", "manager_flat"]);
        reloadedCommit.Commit.Changes.Should().ContainKey("Contact/$type");
        reloadedCommit.Commit.Changes.Should().ContainKey($"Orders/{BaseOrderId:D}").WhoseValue.Should().Be(KVValue.Tombstone);

        reloadedSnapshot.Should().NotBeNull();
        var finalRoot = Bind(KVOverlay.Create(reloadedSnapshot!.Snapshot, "reader"));
        AssertDraftGraph(finalRoot);
    }

    [Fact]
    public async Task RepeatedReloadEditSaveReload_DoesNotDriftStoredValueTypes()
    {
        var snapshot = await PersistSnapshotAsync(CreateFullSnapshot());

        var firstOverlay = KVOverlay.Create(snapshot.Clone(), "adjuster-a");
        var firstDraft = Bind(firstOverlay);
        firstDraft.Price = 1000.01m;
        firstDraft.DateLookingText = "2030-12-31";
        firstDraft.Details = new ComplexDetails("first", [1, 2, 3], [new MetricValue("first", 1.25m)]);
        firstDraft.SmartStatus = IntegrationSmartStatus.New;
        firstDraft.CompensationType = IntegrationCompensationType.Manager;

        var overlayDocument = IntegrationOverlayDocument.Create(AggregateId, "adjuster-a", firstOverlay);
        await using (var session = Store.LightweightSession())
        {
            session.Store(overlayDocument);
            await session.SaveChangesAsync();
        }

        IntegrationOverlayDocument reloadedOverlay;
        await using (var session = Store.QuerySession())
        {
            reloadedOverlay = (await session.LoadAsync<IntegrationOverlayDocument>(overlayDocument.Id))!;
        }

        var secondOverlay = reloadedOverlay.ToOverlay();
        var secondDraft = Bind(secondOverlay);
        secondDraft.Price = 2000.02m;
        secondDraft.DateTimeValue = new DateTime(2031, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        secondDraft.Tags = ["second", "reload"];
        secondDraft.Patch(
            KVPatchOperation.Set("/SmartStatus", "approved"),
            KVPatchOperation.Set("/CompensationType", "assistant_hourly"));
        reloadedOverlay.UpdateFrom(secondOverlay);

        await using (var session = Store.LightweightSession())
        {
            session.Store(reloadedOverlay);
            await session.SaveChangesAsync();
        }

        IntegrationOverlayDocument finalOverlayDocument;
        await using (var session = Store.QuerySession())
        {
            finalOverlayDocument = (await session.LoadAsync<IntegrationOverlayDocument>(overlayDocument.Id))!;
        }

        finalOverlayDocument.Changes["Price"].Should().BeOfType<KVValue<decimal>>();
        finalOverlayDocument.Changes["DateLookingText"].Should().BeOfType<KVValue<string>>();
        finalOverlayDocument.Changes["DateTimeValue"].Should().BeOfType<KVValue<DateTime>>();
        finalOverlayDocument.Changes["Tags"].Should().BeOfType<KVValue<string[]>>();
        AssertStoredString(finalOverlayDocument.Changes["SmartStatus"], "approved");
        AssertStoredString(finalOverlayDocument.Changes["CompensationType"], "assistant_hourly");

        var finalRoot = Bind(finalOverlayDocument.ToOverlay());
        finalRoot.Price.Should().Be(2000.02m);
        finalRoot.DateLookingText.Should().Be("2030-12-31");
        finalRoot.DateTimeValue.Should().Be(new DateTime(2031, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        finalRoot.Tags.Should().BeEquivalentTo(["second", "reload"]);
        finalRoot.Details.Should().BeEquivalentTo(new ComplexDetails("first", [1, 2, 3], [new MetricValue("first", 1.25m)]));
        finalRoot.SmartStatus.Should().Be(IntegrationSmartStatus.Approved);
        finalRoot.CompensationType.Should().Be(IntegrationCompensationType.Assistant);
    }

    [Fact]
    public async Task CollectionOrder_AfterMoveAndCommit_IsPreservedThroughDatabase()
    {
        // Three orders created in a specific order, then one is moved.
        var orderId1 = Guid.Parse("aaaa0001-0000-0000-0000-000000000000");
        var orderId2 = Guid.Parse("aaaa0002-0000-0000-0000-000000000000");
        var orderId3 = Guid.Parse("aaaa0003-0000-0000-0000-000000000000");

        var snapshot = new KVSnapshot { CreatedBy = "test", ModifiedBy = "test" };
        var overlay = KVOverlay.Create(snapshot, "test");
        var root = Bind(overlay);
        var o1 = root.Orders.Create(orderId1); o1.OrderNumber = "ORD-1";
        var o2 = root.Orders.Create(orderId2); o2.OrderNumber = "ORD-2";
        var o3 = root.Orders.Create(orderId3); o3.OrderNumber = "ORD-3";

        // Move third order to front: [orderId3, orderId1, orderId2]
        root.Orders.MoveById(orderId3.ToString("D"), 0);

        var commit = root.CreateCommit(DateTimeOffset.UtcNow);
        commit.User = "test";
        snapshot.Apply(commit);

        // Persist and reload from Postgres
        await using (var session = Store.LightweightSession())
        {
            session.Store(new IntegrationSnapshotDocument { Id = AggregateId, Snapshot = snapshot });
            await session.SaveChangesAsync();
        }

        IntegrationSnapshotDocument? reloaded;
        await using (var session = Store.QuerySession())
        {
            reloaded = await session.LoadAsync<IntegrationSnapshotDocument>(AggregateId);
        }

        reloaded.Should().NotBeNull();
        var reloadedRoot = Bind(KVOverlay.Create(reloaded!.Snapshot, "reader"));

        var ids = reloadedRoot.Orders.GetActiveItemIds();
        ids.Should().BeEquivalentTo(
            new[] { orderId3.ToString("D"), orderId1.ToString("D"), orderId2.ToString("D") },
            options => options.WithStrictOrdering());
        reloadedRoot.Orders.ElementAt(0).OrderNumber.Should().Be("ORD-3");
        reloadedRoot.Orders.ElementAt(1).OrderNumber.Should().Be("ORD-1");
        reloadedRoot.Orders.ElementAt(2).OrderNumber.Should().Be("ORD-2");
    }

    private async Task<KVSnapshot> PersistSnapshotAsync(KVSnapshot snapshot)
    {
        await using var session = Store.LightweightSession();
        session.Store(new IntegrationSnapshotDocument { Id = AggregateId, Snapshot = snapshot });
        await session.SaveChangesAsync();

        await using var query = Store.QuerySession();
        var reloaded = await query.LoadAsync<IntegrationSnapshotDocument>(AggregateId);
        return reloaded!.Snapshot;
    }

    private static KVSnapshot CreateFullSnapshot()
    {
        var snapshot = new KVSnapshot
        {
            CreatedBy = "creator",
            ModifiedBy = "creator"
        };
        var overlay = KVOverlay.Create(snapshot, "creator");
        var root = Bind(overlay);

        root.Text = "base text";
        root.DateLookingText = "2026-06-03";
        root.Flag = true;
        root.Count = 42;
        root.LongCount = 9_999_999_999;
        root.Ratio = 123.456;
        root.Price = 789.10m;
        root.ExternalId = ExternalId;
        root.DateOnlyValue = new DateOnly(2026, 6, 3);
        root.DateTimeValue = new DateTime(2026, 6, 3, 10, 11, 12, DateTimeKind.Utc);
        root.DateTimeOffsetValue = new DateTimeOffset(2026, 6, 3, 13, 14, 15, TimeSpan.FromHours(2));
        root.TimeOnlyValue = new TimeOnly(16, 17, 18);
        root.Duration = TimeSpan.FromMinutes(90);
        root.OptionalNumber = 7;
        root.Tags = ["alpha", "beta"];
        root.Metrics = [new MetricValue("height", 12.34m), new MetricValue("width", 56.78m)];
        root.Details = new ComplexDetails("base", [10, 20], [new MetricValue("nested", 3.14m)]);
        root.SmartStatus = IntegrationSmartStatus.InReview;
        root.CompensationType = IntegrationCompensationType.Assistant;
        root.Profile.DisplayName = "Base Profile";
        root.Profile.Address.Line1 = "1 Integration Way";
        root.Profile.Address.City = "Testville";

        var order = root.Orders.Create(BaseOrderId);
        order.OrderNumber = "ORD-001";
        var line = order.Lines.Create(BaseLineId);
        line.Sku = "SKU-001";
        line.Quantity = 2;
        var adjustment = line.Adjustments.Create(BaseAdjustmentId);
        adjustment.Reason = "base-discount";
        adjustment.Amount = -1.25m;
        adjustment.StatusHistory = [IntegrationSmartStatus.New, IntegrationSmartStatus.Approved];
        adjustment.StatusList = [IntegrationSmartStatus.InReview, IntegrationSmartStatus.Approved];
        adjustment.CompensationHistory = [IntegrationCompensationType.Assistant, IntegrationCompensationType.Manager];
        adjustment.CompensationList = [IntegrationCompensationType.Manager, IntegrationCompensationType.Assistant];

        root.Patch(KVPatchOperation.Init("/Contact", "PERSON"));
        var person = (PersonIntegrationContact)root.Contact!;
        person.FullName = "Jane Base";
        person.StatusHistory = [IntegrationSmartStatus.New, IntegrationSmartStatus.InReview];
        person.StatusList = [IntegrationSmartStatus.InReview, IntegrationSmartStatus.Approved];
        person.CompensationHistory = [IntegrationCompensationType.Assistant, IntegrationCompensationType.Manager];
        person.CompensationList = [IntegrationCompensationType.Manager, IntegrationCompensationType.Assistant];

        var commit = root.CreateCommit(DateTimeOffset.Parse("2026-06-03T07:00:00+00:00"));
        commit.User = "creator";
        snapshot.Apply(commit);
        return snapshot;
    }

    private static void ApplyDraftEdits(IntegrationGraph root)
    {
        root.Text = "draft text";
        root.DateTimeValue = new DateTime(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        root.Price = 9876.54m;
        root.OptionalNumber = null;
        root.Tags = ["draft", "tags"];
        root.Details = new ComplexDetails("draft", [30, 40], [new MetricValue("edited", 9.99m)]);
        root.Patch(
            KVPatchOperation.Set("/SmartStatus", "approved"),
            KVPatchOperation.Set("/CompensationType", "manager_flat"));
        root.Profile.Address.City = "Draft City";

        root.Patch(KVPatchOperation.Remove($"/Orders/{BaseOrderId:D}"));
        var order = root.Orders.Create(DraftOrderId);
        order.OrderNumber = "ORD-002";
        var line = order.Lines.Create(DraftLineId);
        line.Sku = "SKU-002";
        line.Quantity = 5;
        var adjustment = line.Adjustments.Create(DraftAdjustmentId);
        adjustment.Reason = "draft-surcharge";
        adjustment.Amount = 2.50m;
        root.Patch(
            KVPatchOperation.Set($"/Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/StatusHistory", new[] { "approved", "new" }),
            KVPatchOperation.Set($"/Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/StatusList", new[] { "new", "in_review" }),
            KVPatchOperation.Set($"/Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/CompensationHistory", new[] { "manager_flat", "assistant_hourly" }),
            KVPatchOperation.Set($"/Orders/{DraftOrderId:D}/Lines/{DraftLineId:D}/Adjustments/{DraftAdjustmentId:D}/CompensationList", new[] { "assistant_hourly", "manager_flat" }));

        root.Patch(KVPatchOperation.Init("/Contact", "COMPANY"));
        ((CompanyIntegrationContact)root.Contact!).CompanyName = "Draft Corp";
    }

    private static IntegrationGraph Bind(KVOverlay overlay)
    {
        return KVRootNode.Create<IntegrationGraph>(overlay, Definition);
    }

    private static void AssertFullGraph(IntegrationGraph root)
    {
        root.Text.Should().Be("base text");
        root.DateLookingText.Should().Be("2026-06-03");
        root.Flag.Should().BeTrue();
        root.Count.Should().Be(42);
        root.LongCount.Should().Be(9_999_999_999);
        root.Ratio.Should().Be(123.456);
        root.Price.Should().Be(789.10m);
        root.ExternalId.Should().Be(ExternalId);
        root.DateOnlyValue.Should().Be(new DateOnly(2026, 6, 3));
        root.DateTimeValue.Should().Be(new DateTime(2026, 6, 3, 10, 11, 12, DateTimeKind.Utc));
        root.DateTimeOffsetValue.Should().Be(new DateTimeOffset(2026, 6, 3, 13, 14, 15, TimeSpan.FromHours(2)));
        root.TimeOnlyValue.Should().Be(new TimeOnly(16, 17, 18));
        root.Duration.Should().Be(TimeSpan.FromMinutes(90));
        root.OptionalNumber.Should().Be(7);
        root.Tags.Should().BeEquivalentTo(["alpha", "beta"]);
        root.Metrics.Should().BeEquivalentTo([new MetricValue("height", 12.34m), new MetricValue("width", 56.78m)]);
        root.Details.Should().BeEquivalentTo(new ComplexDetails("base", [10, 20], [new MetricValue("nested", 3.14m)]));
        root.SmartStatus.Should().Be(IntegrationSmartStatus.InReview);
        root.CompensationType.Should().Be(IntegrationCompensationType.Assistant);
        root.Profile.DisplayName.Should().Be("Base Profile");
        root.Profile.Address.Line1.Should().Be("1 Integration Way");
        root.Profile.Address.City.Should().Be("Testville");

        var order = root.Orders.GetById(BaseOrderId.ToString("D"));
        order.Should().NotBeNull();
        order!.OrderNumber.Should().Be("ORD-001");
        var line = order.Lines.GetById(BaseLineId.ToString("D"));
        line.Should().NotBeNull();
        line!.Sku.Should().Be("SKU-001");
        line.Quantity.Should().Be(2);
        var adjustment = line.Adjustments.GetById(BaseAdjustmentId.ToString("D"));
        adjustment.Should().NotBeNull();
        adjustment!.Reason.Should().Be("base-discount");
        adjustment.Amount.Should().Be(-1.25m);
        adjustment.StatusHistory.Should().BeEquivalentTo([IntegrationSmartStatus.New, IntegrationSmartStatus.Approved], options => options.WithStrictOrdering());
        adjustment.StatusList.Should().BeEquivalentTo([IntegrationSmartStatus.InReview, IntegrationSmartStatus.Approved], options => options.WithStrictOrdering());
        adjustment.CompensationHistory.Should().BeEquivalentTo([IntegrationCompensationType.Assistant, IntegrationCompensationType.Manager], options => options.WithStrictOrdering());
        adjustment.CompensationList.Should().BeEquivalentTo([IntegrationCompensationType.Manager, IntegrationCompensationType.Assistant], options => options.WithStrictOrdering());
        var contact = root.Contact.Should().BeOfType<PersonIntegrationContact>().Subject;
        contact.FullName.Should().Be("Jane Base");
        contact.StatusHistory.Should().BeEquivalentTo([IntegrationSmartStatus.New, IntegrationSmartStatus.InReview], options => options.WithStrictOrdering());
        contact.StatusList.Should().BeEquivalentTo([IntegrationSmartStatus.InReview, IntegrationSmartStatus.Approved], options => options.WithStrictOrdering());
        contact.CompensationHistory.Should().BeEquivalentTo([IntegrationCompensationType.Assistant, IntegrationCompensationType.Manager], options => options.WithStrictOrdering());
        contact.CompensationList.Should().BeEquivalentTo([IntegrationCompensationType.Manager, IntegrationCompensationType.Assistant], options => options.WithStrictOrdering());
    }

    private static void AssertDraftGraph(IntegrationGraph root)
    {
        root.Text.Should().Be("draft text");
        root.DateLookingText.Should().Be("2026-06-03");
        root.DateTimeValue.Should().Be(new DateTime(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        root.Price.Should().Be(9876.54m);
        root.OptionalNumber.Should().BeNull();
        root.Tags.Should().BeEquivalentTo(["draft", "tags"]);
        root.Details.Should().BeEquivalentTo(new ComplexDetails("draft", [30, 40], [new MetricValue("edited", 9.99m)]));
        root.SmartStatus.Should().Be(IntegrationSmartStatus.Approved);
        root.CompensationType.Should().Be(IntegrationCompensationType.Manager);
        root.Profile.Address.City.Should().Be("Draft City");
        root.Orders.GetById(BaseOrderId.ToString("D")).Should().BeNull();

        var order = root.Orders.GetById(DraftOrderId.ToString("D"));
        order.Should().NotBeNull();
        order!.OrderNumber.Should().Be("ORD-002");
        var line = order.Lines.GetById(DraftLineId.ToString("D"));
        line.Should().NotBeNull();
        line!.Sku.Should().Be("SKU-002");
        line.Quantity.Should().Be(5);
        var adjustment = line.Adjustments.GetById(DraftAdjustmentId.ToString("D"));
        adjustment.Should().NotBeNull();
        adjustment!.Reason.Should().Be("draft-surcharge");
        adjustment.Amount.Should().Be(2.50m);
        adjustment.StatusHistory.Should().BeEquivalentTo([IntegrationSmartStatus.Approved, IntegrationSmartStatus.New], options => options.WithStrictOrdering());
        adjustment.StatusList.Should().BeEquivalentTo([IntegrationSmartStatus.New, IntegrationSmartStatus.InReview], options => options.WithStrictOrdering());
        adjustment.CompensationHistory.Should().BeEquivalentTo([IntegrationCompensationType.Manager, IntegrationCompensationType.Assistant], options => options.WithStrictOrdering());
        adjustment.CompensationList.Should().BeEquivalentTo([IntegrationCompensationType.Assistant, IntegrationCompensationType.Manager], options => options.WithStrictOrdering());
        root.Contact.Should().BeOfType<CompanyIntegrationContact>().Which.CompanyName.Should().Be("Draft Corp");
    }

    private static void AssertStoredString(KVValue storedValue, string expected)
    {
        storedValue.Should().BeOfType<KVValue<string>>()
            .Which.TypedValue.Should().Be(expected);
    }

    private static void AssertStoredJsonArray(KVValue storedValue, string[] expected)
    {
        storedValue.Value.Should().BeAssignableTo<IEnumerable<string>>().Subject
            .Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }
}
