using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Outbound;

/// <summary>
/// Doc §7.3 - which stock records satisfy an order line. Creating one must:
/// leave OnHandQuantity unchanged, increase AllocatedQuantity, and therefore
/// decrease AvailableQuantity.
/// </summary>
public class InventoryAllocation : AuditableEntity
{
    public Guid OrderDetailId { get; set; }
    public OrderDetail OrderDetail { get; set; } = null!;

    /// <summary>The exact balance row the stock is reserved from.</summary>
    public Guid InventoryBalanceId { get; set; }
    public InventoryBalance InventoryBalance { get; set; } = null!;

    public Guid LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid? LotId { get; set; }
    public Lot? Lot { get; set; }

    public Guid? SerialId { get; set; }
    public SerialNumber? Serial { get; set; }

    public Guid? LicensePlateId { get; set; }
    public LicensePlate? LicensePlate { get; set; }

    /// <summary>Reserved quantity in base UOM.</summary>
    public decimal AllocatedQuantity { get; set; }

    /// <summary>Quantity of this allocation already picked.</summary>
    public decimal PickedQuantity { get; set; }

    /// <summary>Which strategy chose this source. Recorded for later algorithm analysis.</summary>
    public AllocationStrategy AllocationStrategy { get; set; } = AllocationStrategy.Manual;

    public AllocationStatus Status { get; set; } = AllocationStatus.Allocated;

    public ICollection<PickTask> PickTasks { get; set; } = new List<PickTask>();

    /// <summary>Allocated quantity not yet covered by a completed pick.</summary>
    public decimal OpenQuantity => Math.Max(0m, AllocatedQuantity - PickedQuantity);
}
