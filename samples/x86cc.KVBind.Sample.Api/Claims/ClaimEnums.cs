namespace x86cc.KVBind.Sample.Api.Claims;

// Smart enum — stores Id in overlay, carries Label for UI display.

public sealed class ClaimStatus : IEquatable<ClaimStatus>
{
    public static readonly ClaimStatus Draft      = new("draft",       "Draft");
    public static readonly ClaimStatus InReview   = new("in_review",   "In Review");
    public static readonly ClaimStatus Approved   = new("approved",    "Approved");
    public static readonly ClaimStatus Rejected   = new("rejected",    "Rejected");
    public static readonly ClaimStatus Closed     = new("closed",      "Closed");

    public static IReadOnlyList<ClaimStatus> All { get; } = [Draft, InReview, Approved, Rejected, Closed];

    private ClaimStatus(string id, string label) { Id = id; Label = label; }
    public string Id { get; }
    public string Label { get; }

    public bool Equals(ClaimStatus? other) => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ClaimStatus other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
    public override string ToString() => Label;
}

public sealed class ClaimPriority : IEquatable<ClaimPriority>
{
    public static readonly ClaimPriority Low      = new("low",      "Low");
    public static readonly ClaimPriority Medium   = new("medium",   "Medium");
    public static readonly ClaimPriority High     = new("high",     "High");
    public static readonly ClaimPriority Critical = new("critical", "Critical");

    public static IReadOnlyList<ClaimPriority> All { get; } = [Low, Medium, High, Critical];

    private ClaimPriority(string id, string label) { Id = id; Label = label; }
    public string Id { get; }
    public string Label { get; }

    public bool Equals(ClaimPriority? other) => other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ClaimPriority other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
    public override string ToString() => Label;
}

// Validation profile for claim submission — stricter rules than the default draft profile.
public sealed record SubmitClaimValidationProfile : KVBind.Core.KVValidationProfile;
