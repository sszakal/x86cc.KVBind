using x86cc.KVBind.Core;

namespace x86cc.KVBind.Sample.Api.Claims;

public partial class InsuranceClaim : KVRootNode
{
    [KVBind(nameof(ClaimNumber))]
    public partial string? ClaimNumber { get; set; }

    [KVBind(nameof(Status))]
    public partial string? Status { get; set; }

    [KVBind(nameof(IncidentDate))]
    public partial string? IncidentDate { get; set; }

    [KVBind(nameof(Description))]
    public partial string? Description { get; set; }

    [KVBind(nameof(Priority))]
    public partial string? Priority { get; set; }

    [KVBind(nameof(ClaimedTotal))]
    public partial decimal ClaimedTotal { get; set; }

    [KVBind(nameof(Policy))]
    public ClaimPolicy Policy { get; } = new();

    [KVBind(nameof(DamagedItems))]
    public KVCollectionNode<DamagedItem> DamagedItems { get; } = new();

    [KVBind(nameof(Notes))]
    public KVCollectionNode<ClaimNote> Notes { get; } = new();

    [KVBind(nameof(Claimant))]
    public partial Claimant? Claimant { get; private set; }

    public void RecalculateClaimedTotal(KVChangeContext<InsuranceClaim> context)
    {
        ClaimedTotal = DamagedItems.Sum(item => item.EstimatedAmount);
    }
}

public partial class ClaimPolicy : KVFieldGroupNode
{
    [KVBind(nameof(PolicyNumber))]
    public partial string? PolicyNumber { get; set; }

    [KVBind(nameof(CoverageType))]
    public partial string? CoverageType { get; set; }
}

public partial class DamagedItem : KVCollectionItemNode
{
    [KVBind(nameof(Description))]
    public partial string? Description { get; set; }

    [KVBind(nameof(Category))]
    public partial string? Category { get; set; }

    [KVBind(nameof(EstimatedAmount))]
    public partial decimal EstimatedAmount { get; set; }
}

public partial class ClaimNote : KVCollectionItemNode
{
    [KVBind(nameof(Text))]
    public partial string? Text { get; set; }
}

public abstract partial class Claimant : KVNestedNode;

public partial class PersonClaimant : Claimant
{
    [KVBind(nameof(FullName))]
    public partial string? FullName { get; set; }
}

public partial class CompanyClaimant : Claimant
{
    [KVBind(nameof(CompanyName))]
    public partial string? CompanyName { get; set; }
}
