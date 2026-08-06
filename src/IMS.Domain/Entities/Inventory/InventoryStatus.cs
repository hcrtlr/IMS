using IMS.Domain.Common;

namespace IMS.Domain.Entities.Inventory;

/// <summary>
/// Doc §5.2 - usability state of stock. "Stock can be physically present in the warehouse
/// yet not be allocatable to orders."
///
/// Modelled as a lookup table rather than an enum because InventoryBalance and
/// InventoryTransaction reference it by Id (InventoryStatusId, From/ToInventoryStatusId).
/// The seven documented statuses are seeded; IsAllocatable is what enforces rule §11.4.
/// </summary>
public class InventoryStatus : AuditableEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>
    /// Doc §11.4 - hold, damaged and expired stock must not be allocated to normal orders.
    /// Only statuses flagged here can back an allocation.
    /// </summary>
    public bool IsAllocatable { get; set; }

    /// <summary>
    /// Whether stock in this status counts toward physical on-hand for reporting.
    /// All seeded statuses are physically present; kept for future virtual statuses.
    /// </summary>
    public bool IsPhysicalStock { get; set; } = true;

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Well-known codes, seeded in InventoryStatusSeed.
    public const string Available = "AVAILABLE";
    public const string QualityHold = "QUALITY_HOLD";
    public const string Damaged = "DAMAGED";
    public const string Expired = "EXPIRED";
    public const string Quarantine = "QUARANTINE";
    public const string Returned = "RETURNED";
    public const string Blocked = "BLOCKED";
}
