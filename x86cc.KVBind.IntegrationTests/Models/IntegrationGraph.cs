using x86cc.KVBind.Core;

namespace x86cc.KVBind.IntegrationTests.Models;

public partial class IntegrationGraph : KVRootNode
{
    [KVBind(nameof(Text))]
    public partial string? Text { get; set; }

    [KVBind(nameof(DateLookingText))]
    public partial string? DateLookingText { get; set; }

    [KVBind(nameof(Flag))]
    public partial bool Flag { get; set; }

    [KVBind(nameof(Count))]
    public partial int Count { get; set; }

    [KVBind(nameof(LongCount))]
    public partial long LongCount { get; set; }

    [KVBind(nameof(Ratio))]
    public partial double Ratio { get; set; }

    [KVBind(nameof(Price))]
    public partial decimal Price { get; set; }

    [KVBind(nameof(ExternalId))]
    public partial Guid ExternalId { get; set; }

    [KVBind(nameof(DateOnlyValue))]
    public partial DateOnly DateOnlyValue { get; set; }

    [KVBind(nameof(DateTimeValue))]
    public partial DateTime DateTimeValue { get; set; }

    [KVBind(nameof(DateTimeOffsetValue))]
    public partial DateTimeOffset DateTimeOffsetValue { get; set; }

    [KVBind(nameof(TimeOnlyValue))]
    public partial TimeOnly TimeOnlyValue { get; set; }

    [KVBind(nameof(Duration))]
    public partial TimeSpan Duration { get; set; }

    [KVBind(nameof(OptionalNumber))]
    public partial int? OptionalNumber { get; set; }

    [KVBind(nameof(Tags))]
    public partial string[]? Tags { get; set; }

    [KVBind(nameof(Metrics))]
    public partial List<MetricValue>? Metrics { get; set; }

    [KVBind(nameof(Details))]
    public partial ComplexDetails? Details { get; set; }

    [KVBind(nameof(SmartStatus))]
    public partial IntegrationSmartStatus? SmartStatus { get; set; }

    [KVBind(nameof(CompensationType))]
    public partial IntegrationCompensationType CompensationType { get; set; }

    [KVBind(nameof(Profile))]
    public IntegrationProfile Profile { get; } = new();

    [KVBind(nameof(Orders))]
    public KVCollectionNode<IntegrationOrder> Orders { get; } = new();

    [KVBind(nameof(Contact))]
    public partial IntegrationContact? Contact { get; private set; }
}

public partial class IntegrationProfile : KVFieldGroupNode
{
    [KVBind(nameof(DisplayName))]
    public partial string? DisplayName { get; set; }

    [KVBind(nameof(Address))]
    public IntegrationAddress Address { get; } = new();
}

public partial class IntegrationAddress : KVFieldGroupNode
{
    [KVBind(nameof(Line1))]
    public partial string? Line1 { get; set; }

    [KVBind(nameof(City))]
    public partial string? City { get; set; }
}

public partial class IntegrationOrder : KVCollectionItemNode
{
    [KVBind(nameof(OrderNumber))]
    public partial string? OrderNumber { get; set; }

    [KVBind(nameof(Lines))]
    public KVCollectionNode<IntegrationOrderLine> Lines { get; } = new();
}

public partial class IntegrationOrderLine : KVCollectionItemNode
{
    [KVBind(nameof(Sku))]
    public partial string? Sku { get; set; }

    [KVBind(nameof(Quantity))]
    public partial int Quantity { get; set; }

    [KVBind(nameof(Adjustments))]
    public KVCollectionNode<IntegrationAdjustment> Adjustments { get; } = new();
}

public partial class IntegrationAdjustment : KVCollectionItemNode
{
    [KVBind(nameof(Reason))]
    public partial string? Reason { get; set; }

    [KVBind(nameof(Amount))]
    public partial decimal Amount { get; set; }

    [KVBind(nameof(StatusHistory))]
    public partial IntegrationSmartStatus[]? StatusHistory { get; set; }

    [KVBind(nameof(StatusList))]
    public partial List<IntegrationSmartStatus>? StatusList { get; set; }

    [KVBind(nameof(CompensationHistory))]
    public partial IntegrationCompensationType[]? CompensationHistory { get; set; }

    [KVBind(nameof(CompensationList))]
    public partial List<IntegrationCompensationType>? CompensationList { get; set; }
}

public abstract partial class IntegrationContact : KVNestedNode;

public partial class PersonIntegrationContact : IntegrationContact
{
    [KVBind(nameof(FullName))]
    public partial string? FullName { get; set; }

    [KVBind(nameof(StatusHistory))]
    public partial IntegrationSmartStatus[]? StatusHistory { get; set; }

    [KVBind(nameof(StatusList))]
    public partial List<IntegrationSmartStatus>? StatusList { get; set; }

    [KVBind(nameof(CompensationHistory))]
    public partial IntegrationCompensationType[]? CompensationHistory { get; set; }

    [KVBind(nameof(CompensationList))]
    public partial List<IntegrationCompensationType>? CompensationList { get; set; }
}

public partial class CompanyIntegrationContact : IntegrationContact
{
    [KVBind(nameof(CompanyName))]
    public partial string? CompanyName { get; set; }
}

public sealed record MetricValue(string Name, decimal Amount);

public sealed record ComplexDetails(string Code, int[] Scores, IReadOnlyList<MetricValue> Metrics);

public sealed class IntegrationSmartStatus : IEquatable<IntegrationSmartStatus>
{
    public static readonly IntegrationSmartStatus New = new("new", "New");
    public static readonly IntegrationSmartStatus InReview = new("in_review", "In Review");
    public static readonly IntegrationSmartStatus Approved = new("approved", "Approved");

    public static IReadOnlyList<IntegrationSmartStatus> All { get; } =
    [
        New,
        InReview,
        Approved
    ];

    private IntegrationSmartStatus(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }

    public string Label { get; }

    public bool Equals(IntegrationSmartStatus? other)
    {
        return other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is IntegrationSmartStatus other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(Id);
    }

    public override string ToString()
    {
        return Label;
    }
}

public enum IntegrationCompensationType
{
    None,
    Manager,
    Assistant
}
