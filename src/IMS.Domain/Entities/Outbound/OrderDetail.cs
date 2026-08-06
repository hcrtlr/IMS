using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Outbound;

/// <summary>Doc §7.2 - a product line on a customer order.</summary>
public class OrderDetail : AuditableEntity
{
    public Guid OrderId { get; set; }
    public OrderMaster Order { get; set; } = null!;

    public int LineNumber { get; set; }

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    /// <summary>Quantity ordered, expressed in UomId.</summary>
    public decimal OrderedQuantity { get; set; }

    /// <summary>Reserved so far, in base UOM. Doc §7.3 - grows on allocation only.</summary>
    public decimal AllocatedQuantity { get; set; }

    /// <summary>Physically picked so far, in base UOM.</summary>
    public decimal PickedQuantity { get; set; }

    /// <summary>Shipped so far, in base UOM.</summary>
    public decimal ShippedQuantity { get; set; }

    public Guid UomId { get; set; }
    public UnitOfMeasure Uom { get; set; } = null!;

    /// <summary>Customer demands this exact lot; restricts allocation candidates.</summary>
    public string? RequiredLotNumber { get; set; }

    /// <summary>Customer demands this exact serial; restricts allocation candidates.</summary>
    public string? RequiredSerialNumber { get; set; }

    /// <summary>
    /// Minimum remaining shelf life the allocated lot must have. Filters lots whose
    /// expiration is too close - the groundwork for FEFO.
    /// </summary>
    public int? MinimumShelfLifeDays { get; set; }

    public OrderDetailStatus Status { get; set; } = OrderDetailStatus.Open;

    public ICollection<InventoryAllocation> Allocations { get; set; } = new List<InventoryAllocation>();
    public ICollection<PickTask> PickTasks { get; set; } = new List<PickTask>();

    /// <summary>Quantity still needing allocation, in base UOM.</summary>
    public decimal UnallocatedQuantity => Math.Max(0m, OrderedQuantity - AllocatedQuantity);

    public bool IsFullyAllocated => AllocatedQuantity >= OrderedQuantity;

    /// <summary>Recomputes line status from its own quantities.</summary>
    public void RecalculateStatus()
    {
        if (Status == OrderDetailStatus.Cancelled) return;

        Status = ShippedQuantity >= OrderedQuantity && ShippedQuantity > 0 ? OrderDetailStatus.Shipped
            : PickedQuantity >= AllocatedQuantity && PickedQuantity > 0 ? OrderDetailStatus.Picked
            : PickedQuantity > 0 ? OrderDetailStatus.Picking
            : IsFullyAllocated && AllocatedQuantity > 0 ? OrderDetailStatus.Allocated
            : AllocatedQuantity > 0 ? OrderDetailStatus.PartiallyAllocated
            : OrderDetailStatus.Open;
    }
}
