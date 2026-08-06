using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;

namespace IMS.Domain.Entities.Counting;

/// <summary>
/// Backs POST /api/inventory-adjustments/{id}/approve (§12) and Faz 5's "Inventory
/// adjustment". Section 9 is missing from the source document, so the approval gate is
/// inferred from the endpoint name. See docs/ASSUMPTIONS.md.
///
/// Stock is NOT touched when the adjustment is raised - only on approval, which then
/// writes a CountAdjustment InventoryTransaction (rule §11.9).
/// </summary>
public class InventoryAdjustment : AuditableEntity, IWarehouseScoped, IAccountScoped
{
    public Guid AccountId { get; set; }
    public Guid WarehouseId { get; set; }

    public string AdjustmentNumber { get; set; } = null!;

    public Guid LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid InventoryStatusId { get; set; }
    public InventoryStatus InventoryStatus { get; set; } = null!;

    public Guid? LotId { get; set; }
    public Lot? Lot { get; set; }

    public Guid? SerialId { get; set; }
    public SerialNumber? Serial { get; set; }

    public Guid? LicensePlateId { get; set; }
    public LicensePlate? LicensePlate { get; set; }

    /// <summary>Balance being corrected. Null when adding stock the system did not know about.</summary>
    public Guid? InventoryBalanceId { get; set; }
    public InventoryBalance? InventoryBalance { get; set; }

    /// <summary>System on-hand at the moment the adjustment was raised.</summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>The corrected on-hand the approver is being asked to accept.</summary>
    public decimal CountedQuantity { get; set; }

    /// <summary>CountedQuantity - SystemQuantity. Signed: negative writes stock off.</summary>
    public decimal AdjustmentQuantity { get; set; }

    public AdjustmentReason Reason { get; set; } = AdjustmentReason.CountVariance;
    public AdjustmentStatus Status { get; set; } = AdjustmentStatus.Pending;

    /// <summary>Set when the adjustment came from a count task.</summary>
    public Guid? CountTaskId { get; set; }

    public string? RequestedBy { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? Notes { get; set; }

    /// <summary>Correlates the InventoryTransaction written when this is approved.</summary>
    public Guid CorrelationId { get; set; }

    private static readonly Dictionary<AdjustmentStatus, AdjustmentStatus[]> Allowed = new()
    {
        [AdjustmentStatus.Pending] = [AdjustmentStatus.Approved, AdjustmentStatus.Rejected, AdjustmentStatus.Cancelled],
        [AdjustmentStatus.Approved] = [],
        [AdjustmentStatus.Rejected] = [],
        [AdjustmentStatus.Cancelled] = []
    };

    public void TransitionTo(AdjustmentStatus target)
    {
        if (!Allowed.TryGetValue(Status, out var next) || !next.Contains(target))
            throw new InvalidStateTransitionException(nameof(InventoryAdjustment), Status.ToString(), target.ToString());

        Status = target;
    }

    /// <summary>True when approving this adds stock rather than removing it.</summary>
    public bool IsIncrease => AdjustmentQuantity > 0m;
}
