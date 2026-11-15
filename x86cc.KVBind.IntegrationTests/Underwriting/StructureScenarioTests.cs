using AwesomeAssertions;
using x86cc.KVBind.Core;
using x86cc.KVBind.Sample.Api.Underwriting;

namespace x86cc.KVBind.IntegrationTests.Underwriting;

// 1d profiles and group rules · 1e field groups · 1f collections · 1g AllowOperations / PatchReadOnly
public sealed class StructureScenarioTests : UnderwritingTestBase
{
    // ── 1d ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void DraftProfile_IsLenient_SubmitProfile_RequiresTheSubmissionData()
    {
        var application = CreateNew();
        Errors(application).Should().BeEmpty("a fresh draft passes the default profile");

        application.Patch(Op("SUBMIT", "/"));

        Errors(application).Should().Contain(new[]
        {
            ("Basics/ApplicantName", "required"),
            ("Basics/EffectiveDate", "required"),
            ("Basics/AnnualRevenue", "required"),
            ("Contact/Notes", "required"),
            ("Insured/CompanyName", "required"),
            ("Locations", "min_count"),
            ("Contacts", "not_empty"),
            ("Questions/PRIOR_LOSSES/Answer", "required"),
        });
    }

    [Fact]
    public void SubmitProfile_PassesOnceTheDataIsComplete()
    {
        var application = CreateNew();
        MakeSubmittable(application);

        application.Patch(Op("SUBMIT", "/"));

        Errors(application).Should().BeEmpty();
        application.Status.Should().Be("submitted");
    }

    [Fact]
    public void ProfileRules_ApplyTheirValueRules_OnlyInTheirProfile()
    {
        var application = CreateNew();
        application.Patch(Set("/Contact/Notes", "\"short\""));
        Errors(application).Should().BeEmpty();

        MakeSubmittable(application);
        application.Patch(Set("/Contact/Notes", "\"short\""), Op("SUBMIT", "/"));

        Errors(application).Should().ContainSingle().Which.Should().Be(("Contact/Notes", "min_length"));
    }

    [Fact]
    public void WhenRule_RequiresTheVessel_ForMarineRisks_AtTheNestedSlot()
    {
        var application = CreateNew();

        application.Patch(Set("/LineOfBusiness", "\"Marine\""));
        Errors(application).Should().ContainSingle().Which.Should().Be(("Vessel", "required"));

        application.Patch(KVPatchOperation.Init("/Vessel", "YACHT"));
        Errors(application).Should().BeEmpty();

        application.Patch(Set("/LineOfBusiness", "\"Cyber\""), KVPatchOperation.Drop("/Vessel"));
        Errors(application).Should().BeEmpty("the rule only applies to marine risks");
    }

    [Fact]
    public void WhenRule_OnCollectionItems_RequiresDetailsForAYesAnswer()
    {
        var application = CreateNew();

        application.Patch(Set("/Questions/HAZMAT/Answer", "true"));
        Errors(application).Should().ContainSingle().Which.Should().Be(("Questions/HAZMAT/Details", "required"));

        application.Patch(Set("/Questions/HAZMAT/Details", "\"ok\""));
        Errors(application).Should().ContainSingle().Which.Should().Be(("Questions/HAZMAT/Details", "min_length"));
    }

    // ── 1e ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResettableGroup_Reset_WipesNestedGroupsAndReappliesDefaults()
    {
        var application = CreateNew();
        application.Patch(
            Set("/Premises/Construction", "\"steel\""),
            Set("/Premises/Floors", "12"),
            Set("/Premises/YearBuilt", "1999"),
            Set("/Premises/Protection/Sprinklered", "true"),
            Set("/Premises/Protection/AlarmType", "\"central\""));

        application.Patch(KVPatchOperation.Reset("/Premises"));

        application.Premises.Construction.Should().Be("masonry");
        application.Premises.Floors.Should().Be(1);
        application.Premises.YearBuilt.Should().BeNull();
        application.Premises.Protection.Sprinklered.Should().BeFalse();
        application.Premises.Protection.AlarmType.Should().BeNull();
    }

    [Theory]
    [InlineData("/Basics")]
    [InlineData("/Premises/Protection")]
    [InlineData("/")]
    [InlineData("/Basics/ApplicantName")]
    public void Reset_IsRejected_ForGroupsNotDeclaredResettable_TheRootAndFields(string path)
    {
        var application = CreateNew();

        var act = () => application.Patch(KVPatchOperation.Reset(path));

        act.Should().Throw<InvalidOperationException>();
    }

    // ── 1f ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void PolymorphicItems_KeepTheirType_AndItemsHoldTheirOwnCollections()
    {
        var application = CreateNew();

        application.Patch(
            KVPatchOperation.Add("/Locations", new KVAddPatchPayload("hq", "BUILDING")),
            KVPatchOperation.Add("/Locations", new KVAddPatchPayload("van1", "VEHICLE")),
            Set("/Locations/hq/Name", "\"HQ\""),
            Set("/Locations/van1/Name", "\"Van\""),
            Set("/Locations/hq/Address", "\"1 Main St\""),
            Set("/Locations/van1/Vin", "\"1HGCM82633A004352\""),
            KVPatchOperation.Add("/Locations/hq/Tenants", new KVAddPatchPayload("t1")),
            Set("/Locations/hq/Tenants/t1/TenantName", "\"Cafe\""),
            Set("/Locations/hq/Tenants/t1/SharePercent", "40"));

        var reloaded = CommitAndReload(application);
        reloaded.Locations.Get<BuildingLocation>("hq")!.Tenants.Single().TenantName.Should().Be("Cafe");
        reloaded.Locations.Get<VehicleLocation>("van1")!.Vin.Should().Be("1HGCM82633A004352");
    }

    [Fact]
    public void FieldsOfAnotherItemType_AreRejected()
    {
        var application = CreateNew();
        application.Patch(KVPatchOperation.Add("/Locations", new KVAddPatchPayload("van1", "VEHICLE")));

        var act = () => application.Patch(Set("/Locations/van1/Address", "\"1 Main St\""));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SeededItems_AreBornWithTheApplication_InDeclarationOrder()
    {
        var application = CreateNew();

        application.Questions.GetActiveItemIds().Should().Equal("PRIOR_LOSSES", "BANKRUPTCY", "HAZMAT");
        application.Questions.GetById("HAZMAT")!.Prompt.Should().Be("Are hazardous materials stored on site?");
    }

    [Fact]
    public void CountAndAggregateRules_ReportAtTheCollection()
    {
        var application = CreateNew();
        application.Patch(KVPatchOperation.Add("/Locations", new KVAddPatchPayload("hq", "BUILDING")), Set("/Locations/hq/Name", "\"HQ\""));
        foreach (var tenant in new[] { "a", "b" })
        {
            application.Patch(
                KVPatchOperation.Add("/Locations/hq/Tenants", new KVAddPatchPayload(tenant)),
                Set($"/Locations/hq/Tenants/{tenant}/TenantName", $"\"{tenant}\""),
                Set($"/Locations/hq/Tenants/{tenant}/SharePercent", "60"));
        }

        Errors(application).Should().ContainSingle().Which.Should().Be(("Locations/hq/Tenants", "aggregate_less_than_or_equal"));

        application.Patch(Set("/Locations/hq/Value", "60000000"));
        Errors(application).Should().Contain(("Locations", "aggregate_less_than_or_equal"));
    }

    [Fact]
    public void AddRemoveMove_ChangeMembershipAndOrder()
    {
        var application = CreateNew();
        foreach (var id in new[] { "a", "b", "c" })
        {
            application.Patch(KVPatchOperation.Add("/Locations", new KVAddPatchPayload(id, "BUILDING")), Set($"/Locations/{id}/Name", $"\"{id}\""));
        }

        application.Patch(KVPatchOperation.Move("/Locations/c", 0), KVPatchOperation.Remove("/Locations/b"));

        CommitAndReload(application).Locations.GetActiveItemIds().Should().Equal("c", "a");
    }

    [Fact]
    public void ResetOfAnItem_KeepsItsIdAndPosition()
    {
        var application = CreateNew();
        application.Patch(
            KVPatchOperation.Add("/Contacts", new KVAddPatchPayload("a")),
            KVPatchOperation.Add("/Contacts", new KVAddPatchPayload("b")),
            Set("/Contacts/b/Name", "\"B\""),
            Set("/Contacts/b/Role", "\"owner\""));

        application.Patch(KVPatchOperation.Reset("/Contacts/b"));

        application.Contacts.GetActiveItemIds().Should().Equal("a", "b");
        application.Contacts.GetById("b")!.Name.Should().BeNull();
        application.Contacts.GetById("b")!.Role.Should().BeNull();
    }

    // ── 1g ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ADD")]
    [InlineData("REMOVE")]
    [InlineData("MOVE")]
    [InlineData("RESET")]
    public void SeededQuestions_AreLocked_ButTheirAnswersStayEditable(string operation)
    {
        var application = CreateNew();
        var patch = operation switch
        {
            "ADD" => KVPatchOperation.Add("/Questions", new KVAddPatchPayload("EXTRA")),
            "REMOVE" => KVPatchOperation.Remove("/Questions/HAZMAT"),
            "MOVE" => KVPatchOperation.Move("/Questions/HAZMAT", 0),
            _ => KVPatchOperation.Reset("/Questions/HAZMAT"),
        };

        var act = () => application.Patch(patch);

        act.Should().Throw<InvalidOperationException>();
        application.Patch(Set("/Questions/HAZMAT/Answer", "true"));
        application.Questions.GetById("HAZMAT")!.Answer.Should().BeTrue();
    }

    [Fact]
    public void ResetOfAnItem_IsBlocked_WhenTheItemHasPatchReadOnlyMembers()
    {
        var application = CreateNew();
        application.Patch(KVPatchOperation.Add("/Locations", new KVAddPatchPayload("a", "BUILDING")));

        var act = () => application.Patch(KVPatchOperation.Reset("/Locations/a"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*read-only*", "RESET would wipe the PatchReadOnly Approved flag");
    }

    [Fact]
    public void Contacts_CanBeAddedRemovedAndReset_ButNotMoved()
    {
        var application = CreateNew();
        application.Patch(
            KVPatchOperation.Add("/Contacts", new KVAddPatchPayload("a")),
            KVPatchOperation.Add("/Contacts", new KVAddPatchPayload("b")));

        var move = () => application.Patch(KVPatchOperation.Move("/Contacts/b", 0));
        move.Should().Throw<InvalidOperationException>();

        application.Patch(KVPatchOperation.Remove("/Contacts/a"), Set("/Contacts/b/Name", "\"B\""), KVPatchOperation.Reset("/Contacts/b"));
        application.Contacts.GetActiveItemIds().Should().Equal("b");
        application.Contacts.GetById("b")!.Name.Should().BeNull();
    }

    [Fact]
    public void Discard_ThatWouldChangeMembership_IsRejected_WhenTheCollectionForbidsIt()
    {
        var application = CommitAndReload(CreateNew());

        // The in-code API is not restricted: code may remove a seeded question …
        application.Questions.RemoveById("HAZMAT").Should().BeTrue();

        // … but a client DISCARD of the collection would put it back: an ADD the collection does not allow.
        var discard = () => application.Patch(KVPatchOperation.Discard("/Questions"));
        discard.Should().Throw<InvalidOperationException>().WithMessage("*ADD*");

        // Discarding just the item's data leaves membership ($items) alone, so it is allowed.
        application.Patch(KVPatchOperation.Discard("/Questions/HAZMAT"));
        application.Questions.GetActiveItemIds().Should().Equal("PRIOR_LOSSES", "BANKRUPTCY");

        // Discarding a field change inside an item does not touch membership and is fine.
        application.Patch(Set("/Questions/BANKRUPTCY/Answer", "true"), KVPatchOperation.Discard("/Questions/BANKRUPTCY/Answer"));
        application.Questions.GetById("BANKRUPTCY")!.Answer.Should().BeNull();
    }

    [Theory]
    [InlineData("/Status", "\"bound\"")]
    [InlineData("/TotalInsuredValue", "1")]
    [InlineData("/Rating/Premium", "1")]
    [InlineData("/DeclineReason", "\"x\"")]
    public void PatchReadOnlyMembers_RejectPatches(string path, string json)
    {
        var application = CreateNew();

        var act = () => application.Patch(Set(path, json));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchReadOnlyCollection_RejectsAdd_AndItemsRejectEdits()
    {
        var application = CreateNew();
        application.Patch(Op("ADD_ENDORSEMENT", "/Endorsements", "{\"code\":\"E1\",\"description\":\"Flood sublimit\"}"));
        var id = application.Endorsements.GetActiveItemIds().Single();

        var add = () => application.Patch(KVPatchOperation.Add("/Endorsements", new KVAddPatchPayload("x")));
        var edit = () => application.Patch(Set($"/Endorsements/{id}/Code", "\"E2\""));

        add.Should().Throw<InvalidOperationException>();
        edit.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PatchReadOnlyMembers_CanStillBeChangedInCode()
    {
        var application = CreateNew();

        application.Status = "declined";
        application.Rating.Premium = 10m;

        application.Status.Should().Be("declined");
        application.Rating.Premium.Should().Be(10m);
    }
}
