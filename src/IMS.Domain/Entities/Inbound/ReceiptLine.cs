using IMS.Domain.Common;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Inbound;

/// <summary>
/// One physically received line. Referenced by PutawayTask.ReceiptLineId (§6.4), which
/// is the doc's only direct evidence that receipts are split into lines.
/// </summary>
public class ReceiptLine : AuditableEntity
{
    public Guid ReceiptId { get; set; }
    public Receipt Receipt { get; set; } = null!;

    public Guid InboundOrderDetailId { get; set; }
    public InboundOrderDetail InboundOrderDetail { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    /// <summary>Quantity received in ReceivedUom.</summary>
    public decimal ReceivedQuantity { get; set; }

    public Guid ReceivedUomId { get; set; }
    public UnitOfMeasure ReceivedUom { get; set; } = null!;

    /// <summary>ReceivedQuantity normalised to the item's base UOM; all stock maths uses this.</summary>
    public decimal BaseQuantity { get; set; }

    /// <summary>Lot captured at receipt; required when the item is lot-tracked.</summary>
    public Guid? LotId { get; set; }
    public Lot? Lot { get; set; }

    /// <summary>Serial captured at receipt; required when the item is serial-tracked (rule §11.5).</summary>
    public Guid? SerialId { get; set; }
    public SerialNumber? Serial { get; set; }

    /// <summary>Optional LPN created or referenced at receipt.</summary>
    public Guid? LicensePlateId { get; set; }
    public LicensePlate? LicensePlate { get; set; }

    /// <summary>Status the stock was received into; normally Available, or QualityHold for inspection.</summary>
    public Guid InventoryStatusId { get; set; }
    public InventoryStatus InventoryStatus { get; set; } = null!;

    /// <summary>Base-UOM quantity already covered by putaway tasks, to prevent over-putaway.</summary>
    public decimal PutawayQuantity { get; set; }

    public ICollection<PutawayTask> PutawayTasks { get; set; } = new List<PutawayTask>();

    /// <summary>Base-UOM quantity still sitting in the receiving location.</summary>
    public decimal PendingPutawayQuantity => Math.Max(0m, BaseQuantity - PutawayQuantity);
}
