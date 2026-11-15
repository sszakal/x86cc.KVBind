using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Sample.Api.Underwriting;

namespace x86cc.KVBind.IntegrationTests.Underwriting;

// 1h custom operations · 1i nested nodes · 1j change reactions · 1k domain events
public sealed class BehaviourScenarioTests : UnderwritingTestBase
{
    // ── 1h ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Lifecycle_RunsThroughRootOperations_ThatBypassPatchReadOnly()
    {
        var application = CreateNew();
        MakeSubmittable(application);

        application.Patch(Op("SUBMIT", "/"));
        application.Patch(Op("QUOTE", "/", "{\"baseRate\": 2.5, \"modifier\": \"1.2\"}"));

        application.Status.Should().Be("quoted");
        application.Rating.BaseRate.Should().Be(2.5m);
        application.Rating.Premium.Should().Be(3000m, "1,000,000 / 1,000 × 2.5 × 1.2");

        application.Patch(Op("BIND", "/"));
        application.Status.Should().Be("bound");
    }

    [Fact]
    public void Operation_ThatRejectsTheState_LeavesNothingBehind()
    {
        var application = CreateNew();

        var bind = () => application.Patch(Op("BIND", "/"));

        bind.Should().Throw<InvalidOperationException>().WithMessage("*quoted*");
        application.Status.Should().Be("draft");
    }

    [Theory]
    [InlineData("QUOTE", null)] // needs a payload
    [InlineData("QUOTE", "{\"baseRate\": \"x\"}")] // payload of the wrong shape
    [InlineData("SUBMIT", "\"unexpected\"")] // takes no payload
    [InlineData("DECLINE", "\"\"")] // the method rejects an empty reason
    [InlineData("SHRED", null)] // not declared
    public void Operations_ValidateTheirPayload(string operation, string? payload)
    {
        var application = CreateNew();
        application.Status = "submitted";

        var act = () => application.Patch(Op(operation, "/", payload));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ItemOperation_ApprovesOneLocation_AndCollectionOperation_AddsAnEndorsement()
    {
        var application = CreateNew();
        application.Patch(
            KVPatchOperation.Add("/Locations", new KVAddPatchPayload("hq", "BUILDING")),
            KVPatchOperation.Add("/Locations", new KVAddPatchPayload("van", "VEHICLE")));

        application.Patch(Op("APPROVE", "/Locations/van"), Op("ADD_ENDORSEMENT", "/Endorsements", "{\"code\":\"FL-1\"}"));

        application.Locations.GetById("van")!.Approved.Should().BeTrue();
        application.Locations.GetById("hq")!.Approved.Should().BeFalse();
        application.Endorsements.Single().Code.Should().Be("FL-1");
    }

    [Fact]
    public void Decline_SetsTheReason_AndTheWhenRuleChecksIt()
    {
        var application = CreateNew();

        application.Patch(Op("DECLINE", "/", "\"Outside appetite\""));

        application.Status.Should().Be("declined");
        application.DeclineReason.Should().Be("Outside appetite");
        Errors(application).Should().BeEmpty();
    }

    // ── 1i ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void NestedNode_SwitchesSubtype_ByDropAndInit_StartingFresh()
    {
        var application = CreateNew();
        application.Patch(Set("/Insured/CompanyName", "\"Acme\""));

        application.Patch(KVPatchOperation.Drop("/Insured"), KVPatchOperation.Init("/Insured", "PERSON"), Set("/Insured/FullName", "\"Ann Lee\""));

        var reloaded = CommitAndReload(application);
        reloaded.Insured.Should().BeOfType<InsuredPerson>().Which.FullName.Should().Be("Ann Lee");
    }

    [Fact]
    public void NestedNode_InitOverAnInitializedNode_SwitchesSubtypeAndStartsFresh()
    {
        var application = CreateNew();
        application.Patch(Set("/Insured/CompanyName", "\"Acme\""));

        application.Patch(KVPatchOperation.Init("/Insured", "PERSON"));
        application.Patch(KVPatchOperation.Init("/Insured", "COMPANY"));

        application.Insured.Should().BeOfType<InsuredCompany>().Which.CompanyName.Should().BeNull();
    }

    [Fact]
    public void NestedNode_RejectsFieldsOfOtherSubtypes_AndUnknownTypes()
    {
        var application = CreateNew();

        var foreignField = () => application.Patch(Set("/Insured/FullName", "\"x\""));
        var unknownType = () => application.Patch(KVPatchOperation.Init("/Vessel", "SUBMARINE"));
        var fieldOfEmptySlot = () => application.Patch(Set("/Vessel/VesselName", "\"x\""));

        foreignField.Should().Throw<InvalidOperationException>();
        unknownType.Should().Throw<InvalidOperationException>();
        fieldOfEmptySlot.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequiredNestedNode_ReportsAtTheSlot_WhenDropped()
    {
        var application = CreateNew();

        application.Patch(KVPatchOperation.Drop("/Insured"));

        Errors(application).Should().ContainSingle().Which.Should().Be(("Insured", "required"));
    }

    [Fact]
    public void ResetOfANestedNode_KeepsItsSubtype()
    {
        var application = CreateNew();
        application.Patch(Set("/Insured/CompanyName", "\"Acme\""), Set("/Insured/RegistrationNumber", "\"GB123456\""));

        application.Patch(KVPatchOperation.Reset("/Insured"));

        application.Insured.Should().BeOfType<InsuredCompany>().Which.CompanyName.Should().BeNull();
    }

    // ── 1j ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Totals_FollowValueAddRemoveAndMoveChanges()
    {
        var application = CreateNew();

        application.Patch(KVPatchOperation.Add("/Locations", new KVAddPatchPayload("a", "BUILDING")));
        application.LastRecalculation.Should().Be(nameof(KVChangeKind.Added));

        application.Patch(Set("/Locations/a/Value", "100"), KVPatchOperation.Add("/Locations", new KVAddPatchPayload("b", "VEHICLE")), Set("/Locations/b/Value", "50"));
        application.TotalInsuredValue.Should().Be(150m);
        application.LocationCount.Should().Be(2);
        application.LastRecalculation.Should().Be(nameof(KVChangeKind.Value));

        application.Patch(KVPatchOperation.Move("/Locations/b", 0));
        application.LastRecalculation.Should().Be(nameof(KVChangeKind.Moved));

        application.Patch(KVPatchOperation.Remove("/Locations/a"));
        application.TotalInsuredValue.Should().Be(50m);
        application.LastRecalculation.Should().Be(nameof(KVChangeKind.Removed));
    }

    [Fact]
    public void Totals_AreRecomputed_OnReplacedChanges_FromDiscard()
    {
        var application = CreateNew();
        application.Patch(KVPatchOperation.Add("/Locations", new KVAddPatchPayload("a", "BUILDING")), Set("/Locations/a/Name", "\"A\""), Set("/Locations/a/Value", "100"));
        application = CommitAndReload(application);

        application.Patch(Set("/Locations/a/Value", "400"));
        application.TotalInsuredValue.Should().Be(400m);

        application.Patch(KVPatchOperation.Discard("/Locations/a/Value"));
        application.TotalInsuredValue.Should().Be(100m);
        application.LastRecalculation.Should().Be(nameof(KVChangeKind.Replaced));

        application.Patch(KVPatchOperation.Add("/Locations", new KVAddPatchPayload("b", "VEHICLE")), Set("/Locations/b/Value", "5"));
        application.TotalInsuredValue.Should().Be(105m);
        application.Patch(KVPatchOperation.Discard("/Locations"));
        application.TotalInsuredValue.Should().Be(100m, "discarding the collection drops the new item and replaces its data");
        application.LastRecalculation.Should().Be(nameof(KVChangeKind.Replaced));
    }

    [Fact]
    public void Totals_FollowChangesMadeInCode_AndKeepThePremiumInStep()
    {
        var application = CreateNew();
        application.Status = "submitted";
        application.Patch(Op("QUOTE", "/", "{\"baseRate\": 1, \"modifier\": 1}"));

        var location = application.Locations.Create<BuildingLocation>("hq");
        location.Value = 2_000_000m;

        application.TotalInsuredValue.Should().Be(2_000_000m);
        application.Rating.Premium.Should().Be(2000m);
    }

    // ── 1k ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Operations_RaiseDomainEvents_InOrder_AndDequeueEmptiesThem()
    {
        var application = CreateNew();
        MakeSubmittable(application);
        application.DequeueEvents();

        application.Patch(Op("APPROVE", "/Locations/hq"), Op("SUBMIT", "/"), Op("QUOTE", "/", "{\"baseRate\":2,\"modifier\":1}"));

        var events = application.DequeueEvents();
        events.Select(e => e.GetType()).Should().Equal(typeof(LocationApproved), typeof(ApplicationSubmitted), typeof(ApplicationQuoted));
        events.OfType<LocationApproved>().Single().LocationId.Should().Be("hq");
        events.OfType<ApplicationQuoted>().Single().Premium.Should().Be(2000m);
        application.DequeueEvents().Should().BeEmpty();
    }

    [Fact]
    public void FailedOperation_RaisesNoEvent()
    {
        var application = CreateNew();

        var bind = () => application.Patch(Op("BIND", "/"));

        bind.Should().Throw<InvalidOperationException>();
        application.PendingEvents.Should().BeEmpty();
    }

    [Fact]
    public void Clear_DropsPendingEvents()
    {
        var application = CreateNew();
        application.Patch(Op("DECLINE", "/", "\"no\""));

        application.Clear();

        application.PendingEvents.Should().BeEmpty();
        application.Status.Should().BeNull("a cleared new application has no draft data at all");
    }
}
