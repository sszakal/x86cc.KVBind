using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api.Underwriting;

// The "everything" model of the sample: a commercial insurance underwriting application with one section
// per KVBind feature area. The DSL is in UnderwritingDefinitionBuilder; the behaviour (operations,
// reactions, domain events) is here, as plain methods of the nodes.

public enum LineOfBusiness
{
    Property,
    Casualty,
    Marine,
    Cyber
}

public enum SalesChannel
{
    Broker,
    Direct,
    Online
}

[Flags]
public enum CoverageOptions
{
    None = 0,
    Fire = 1,
    Flood = 2,
    Theft = 4,
    Liability = 8
}

/// <summary>Validation profile used once the application has been submitted.</summary>
public sealed record SubmitApplicationProfile : KVValidationProfile;

public sealed record QuoteRequest(decimal BaseRate, decimal Modifier);

public sealed record EndorsementRequest(string Code, string? Description);

public partial class UnderwritingApplication : KVRootNode
{
    public static readonly string[] SubmittedStatuses = ["submitted", "quoted", "bound"];

    // ── Root fields: lifecycle and derived values ───────────────────────────────

    /// <summary>Lifecycle state; PatchReadOnly — only the SUBMIT / QUOTE / BIND / DECLINE operations change it.</summary>
    [KVBind(nameof(Status))]
    public partial string? Status { get; set; }

    [KVBind(nameof(LineOfBusiness))]
    public partial LineOfBusiness LineOfBusiness { get; set; }

    [KVBind(nameof(Tags))]
    public partial string[]? Tags { get; set; }

    [KVBind(nameof(DeclineReason))]
    public partial string? DeclineReason { get; set; }

    /// <summary>Sum of the locations' values; kept by a change reaction, PatchReadOnly.</summary>
    [KVBind(nameof(TotalInsuredValue))]
    public partial decimal TotalInsuredValue { get; set; }

    [KVBind(nameof(LocationCount))]
    public partial int LocationCount { get; set; }

    /// <summary>The kind of the last change the reaction saw (Value, Added, Removed, Moved, Replaced).</summary>
    [KVBind(nameof(LastRecalculation))]
    public partial string? LastRecalculation { get; set; }

    // ── Sections ────────────────────────────────────────────────────────────────

    [KVBind(nameof(Basics))]
    public ApplicationBasics Basics { get; } = new();

    [KVBind(nameof(Classification))]
    public ApplicationClassification Classification { get; } = new();

    [KVBind(nameof(Contact))]
    public ApplicationContact Contact { get; } = new();

    [KVBind(nameof(Premises))]
    public PremisesSection Premises { get; } = new();

    [KVBind(nameof(Program))]
    public ProgramTerms Program { get; } = new();

    [KVBind(nameof(Rating))]
    public RatingSection Rating { get; } = new();

    [KVBind(nameof(Locations))]
    public KVCollectionNode<Location> Locations { get; } = new();

    [KVBind(nameof(Questions))]
    public KVCollectionNode<UnderwritingQuestion> Questions { get; } = new();

    [KVBind(nameof(Contacts))]
    public KVCollectionNode<ContactPerson> Contacts { get; } = new();

    [KVBind(nameof(Endorsements))]
    public KVCollectionNode<Endorsement> Endorsements { get; } = new();

    [KVBind(nameof(Insured))]
    public partial InsuredParty? Insured { get; private set; }

    [KVBind(nameof(Vessel))]
    public partial VesselDetails? Vessel { get; private set; }

    public bool IsSubmitted => this.Status is { } status && SubmittedStatuses.Contains(status, StringComparer.Ordinal);

    // ── Custom operations (1h) — they bypass PatchReadOnly, so they may write Status and Rating ───────

    public void Submit()
    {
        if (this.Status is not ("draft" or null))
        {
            throw new InvalidOperationException($"Only a draft application can be submitted (status is '{this.Status}').");
        }

        this.Status = "submitted";
        this.Raise(new ApplicationSubmitted(this.Basics.ApplicationNumber, this.TotalInsuredValue));
    }

    public void Quote(QuoteRequest request)
    {
        if (request is null)
        {
            throw new InvalidOperationException("QUOTE needs a payload: { baseRate, modifier }.");
        }

        if (this.Status != "submitted" && this.Status != "quoted")
        {
            throw new InvalidOperationException("Only a submitted application can be quoted.");
        }

        if (request.BaseRate <= 0 || request.Modifier <= 0)
        {
            throw new InvalidOperationException("Base rate and modifier must be positive.");
        }

        this.Rating.BaseRate = request.BaseRate;
        this.Rating.Modifier = request.Modifier;
        this.Rating.QuotedAt = DateTimeOffset.UtcNow;
        this.Rating.Recalculate(this.TotalInsuredValue);
        this.Status = "quoted";
        this.Raise(new ApplicationQuoted(this.Rating.Premium ?? 0m));
    }

    public void Bind()
    {
        if (this.Status != "quoted")
        {
            throw new InvalidOperationException("Only a quoted application can be bound.");
        }

        this.Status = "bound";
        this.Raise(new ApplicationBound(this.Rating.Premium ?? 0m));
    }

    public void Decline(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("A decline needs a reason.");
        }

        this.Status = "declined";
        this.DeclineReason = reason;
        this.Raise(new ApplicationDeclined(reason));
    }

    /// <summary>Collection operation on Endorsements (the collection itself is PatchReadOnly).</summary>
    public void AddEndorsement(EndorsementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code, nameof(request));
        var endorsement = this.Endorsements.Create();
        endorsement.Code = request.Code;
        endorsement.Description = request.Description;
        this.Raise(new EndorsementAdded(request.Code));
    }

    // ── Change reactions (1j) ───────────────────────────────────────────────────

    public void RecalculateTotals(KVChangeContext<UnderwritingApplication> context)
    {
        var total = 0m;
        foreach (var location in this.Locations)
        {
            total += location.Value ?? 0m;
        }

        this.TotalInsuredValue = total;
        this.LocationCount = this.Locations.Count();
        this.LastRecalculation = context.Kind.ToString();
        this.Rating.Recalculate(total);
    }

    protected override KVValidationProfile GetValidationProfile() =>
        this.IsSubmitted ? new SubmitApplicationProfile() : KVDefaultValidationProfile.Instance;
}

// ── 1a: scalar types and defaults ─────────────────────────────────────────────

public partial class ApplicationBasics : KVFieldGroupNode
{
    [KVBind(nameof(ApplicationNumber))]
    public partial string? ApplicationNumber { get; set; }

    [KVBind(nameof(ApplicantName))]
    public partial string? ApplicantName { get; set; }

    [KVBind(nameof(YearsInBusiness))]
    public partial int? YearsInBusiness { get; set; }

    [KVBind(nameof(AnnualRevenue))]
    public partial decimal? AnnualRevenue { get; set; }

    [KVBind(nameof(IsRenewal))]
    public partial bool IsRenewal { get; set; }

    [KVBind(nameof(EffectiveDate))]
    public partial DateOnly? EffectiveDate { get; set; }

    [KVBind(nameof(ReceivedAt))]
    public partial DateTimeOffset? ReceivedAt { get; set; }

    [KVBind(nameof(PreferredCallTime))]
    public partial TimeOnly? PreferredCallTime { get; set; }

    [KVBind(nameof(ReviewSla))]
    public partial TimeSpan? ReviewSla { get; set; }

    [KVBind(nameof(BrokerReference))]
    public partial Guid? BrokerReference { get; set; }

    [KVBind(nameof(Channel))]
    public partial SalesChannel? Channel { get; set; }

    [KVBind(nameof(Coverages))]
    public partial CoverageOptions Coverages { get; set; }
}

// ── 1b: allowed values ──────────────────────────────────────────────────────

public partial class ApplicationClassification : KVFieldGroupNode
{
    [KVBind(nameof(Industry))]
    public partial string? Industry { get; set; }

    [KVBind(nameof(RiskTier))]
    public partial string? RiskTier { get; set; }

    [KVBind(nameof(Territory))]
    public partial int? Territory { get; set; }

    [KVBind(nameof(DeductibleOption))]
    public partial string? DeductibleOption { get; set; }

    [KVBind(nameof(Exclusions))]
    public partial string[]? Exclusions { get; set; }
}

// ── 1c: value rules ─────────────────────────────────────────────────────────

public partial class ApplicationContact : KVFieldGroupNode
{
    [KVBind(nameof(Email))]
    public partial string? Email { get; set; }

    [KVBind(nameof(Phone))]
    public partial string? Phone { get; set; }

    [KVBind(nameof(Website))]
    public partial string? Website { get; set; }

    [KVBind(nameof(Employees))]
    public partial int? Employees { get; set; }

    [KVBind(nameof(DiscountPercent))]
    public partial decimal? DiscountPercent { get; set; }

    [KVBind(nameof(InceptionDate))]
    public partial DateOnly? InceptionDate { get; set; }

    [KVBind(nameof(Notes))]
    public partial string? Notes { get; set; }
}

// ── 1e: field groups — nested, resettable ─────────────────────────────────────

public partial class PremisesSection : KVFieldGroupNode
{
    [KVBind(nameof(Construction))]
    public partial string? Construction { get; set; }

    [KVBind(nameof(Floors))]
    public partial int Floors { get; set; }

    [KVBind(nameof(YearBuilt))]
    public partial int? YearBuilt { get; set; }

    [KVBind(nameof(Protection))]
    public ProtectionDetails Protection { get; } = new();
}

public partial class ProtectionDetails : KVFieldGroupNode
{
    [KVBind(nameof(Sprinklered))]
    public partial bool Sprinklered { get; set; }

    [KVBind(nameof(AlarmType))]
    public partial string? AlarmType { get; set; }

    [KVBind(nameof(FireStationDistanceKm))]
    public partial decimal? FireStationDistanceKm { get; set; }
}

// ── 1n: inheritance — program terms come from the parent (master) application ─

public partial class ProgramTerms : KVFieldGroupNode
{
    [KVBind(nameof(ProgramName))]
    public partial string? ProgramName { get; set; }

    [KVBind(nameof(Carrier))]
    public partial string? Carrier { get; set; }

    [KVBind(nameof(CommissionPercent))]
    public partial decimal? CommissionPercent { get; set; }
}

// ── 1g / 1h: rating — PatchReadOnly, written by QUOTE and the reactions ─────────

public partial class RatingSection : KVFieldGroupNode
{
    [KVBind(nameof(BaseRate))]
    public partial decimal? BaseRate { get; set; }

    [KVBind(nameof(Modifier))]
    public partial decimal? Modifier { get; set; }

    [KVBind(nameof(Premium))]
    public partial decimal? Premium { get; set; }

    [KVBind(nameof(QuotedAt))]
    public partial DateTimeOffset? QuotedAt { get; set; }

    // Premium = insured value / 1000 × base rate × modifier, once a rate is set.
    public void Recalculate(decimal totalInsuredValue)
    {
        this.Premium = this.BaseRate is { } rate
            ? decimal.Round(totalInsuredValue / 1000m * rate * (this.Modifier ?? 1m), 2)
            : null;
    }
}

// ── 1f: collections — polymorphic, nested, seeded ───────────────────────────────

/// <summary>A location of the risk; BUILDING or VEHICLE. The common Value feeds the totals.</summary>
public partial class Location : KVCollectionItemNode
{
    [KVBind(nameof(Name))]
    public partial string? Name { get; set; }

    [KVBind(nameof(Value))]
    public partial decimal? Value { get; set; }

    [KVBind(nameof(Approved))]
    public partial bool Approved { get; set; }

    /// <summary>Item operation APPROVE (1h); Approved itself is PatchReadOnly.</summary>
    public void Approve()
    {
        this.Approved = true;
        this.Raise(new LocationApproved(this.ItemId() ?? string.Empty, this.Name));
    }
}

public partial class BuildingLocation : Location
{
    [KVBind(nameof(Address))]
    public partial string? Address { get; set; }

    [KVBind(nameof(Occupancy))]
    public partial string? Occupancy { get; set; }

    /// <summary>A collection inside a collection item.</summary>
    [KVBind(nameof(Tenants))]
    public KVCollectionNode<Tenant> Tenants { get; } = new();
}

public partial class VehicleLocation : Location
{
    [KVBind(nameof(Vin))]
    public partial string? Vin { get; set; }

    [KVBind(nameof(ModelYear))]
    public partial int? ModelYear { get; set; }
}

public partial class Tenant : KVCollectionItemNode
{
    [KVBind(nameof(TenantName))]
    public partial string? TenantName { get; set; }

    [KVBind(nameof(SharePercent))]
    public partial decimal? SharePercent { get; set; }
}

/// <summary>A predefined question; the collection is seeded and its membership locked.</summary>
public partial class UnderwritingQuestion : KVCollectionItemNode
{
    [KVBind(nameof(Prompt))]
    public partial string? Prompt { get; set; }

    [KVBind(nameof(Answer))]
    public partial bool? Answer { get; set; }

    [KVBind(nameof(Details))]
    public partial string? Details { get; set; }
}

public partial class ContactPerson : KVCollectionItemNode
{
    [KVBind(nameof(Name))]
    public partial string? Name { get; set; }

    [KVBind(nameof(Role))]
    public partial string? Role { get; set; }

    [KVBind(nameof(Email))]
    public partial string? Email { get; set; }
}

public partial class Endorsement : KVCollectionItemNode
{
    [KVBind(nameof(Code))]
    public partial string? Code { get; set; }

    [KVBind(nameof(Description))]
    public partial string? Description { get; set; }
}

// ── 1i: nested nodes ──────────────────────────────────────────────────────────

public abstract partial class InsuredParty : KVNestedNode;

public partial class InsuredPerson : InsuredParty
{
    [KVBind(nameof(FullName))]
    public partial string? FullName { get; set; }

    [KVBind(nameof(DateOfBirth))]
    public partial DateOnly? DateOfBirth { get; set; }
}

public partial class InsuredCompany : InsuredParty
{
    [KVBind(nameof(CompanyName))]
    public partial string? CompanyName { get; set; }

    [KVBind(nameof(RegistrationNumber))]
    public partial string? RegistrationNumber { get; set; }
}

public abstract partial class VesselDetails : KVNestedNode;

public partial class Yacht : VesselDetails
{
    [KVBind(nameof(VesselName))]
    public partial string? VesselName { get; set; }

    [KVBind(nameof(LengthMeters))]
    public partial decimal? LengthMeters { get; set; }
}

public partial class CargoShip : VesselDetails
{
    [KVBind(nameof(VesselName))]
    public partial string? VesselName { get; set; }

    [KVBind(nameof(Tonnage))]
    public partial int? Tonnage { get; set; }
}

// ── 1k: domain events ─────────────────────────────────────────────────────────

public sealed record ApplicationSubmitted(string? ApplicationNumber, decimal TotalInsuredValue);

public sealed record ApplicationQuoted(decimal Premium);

public sealed record ApplicationBound(decimal Premium);

public sealed record ApplicationDeclined(string Reason);

public sealed record EndorsementAdded(string Code);

public sealed record LocationApproved(string LocationId, string? Name);
