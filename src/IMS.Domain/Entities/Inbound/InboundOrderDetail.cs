using IMS.Domain.Common;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Inbound;

/// <summary>Doc §6.2 - an expected line on an inbound order.</summary>
public class InboundOrderDetail : AuditableEntity
{
    public Guid InboundOrderId { get; set; }
    public InboundOrder InboundOrder { get; set; } = null!;

    /// <summary>Line ordinal within the order. Not in §6.2 but needed for stable display.</summary>
    public int LineNumber { get; set; }

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    /// <summary>Expected quantity, expressed in UomId.</summary>
    public decimal ExpectedQuantity { get; set; }

    /// <summary>Running total actually received, in base UOM.</summary>
    public decimal ReceivedQuantity { get; set; }

    public Guid UomId { get; set; }
    public UnitOfMeasure Uom { get; set; } = null!;

    public string? ExpectedLotNumber { get; set; }
    public DateTimeOffset? ExpectedExpirationDate { get; set; }

    public ICollection<ReceiptLine> ReceiptLines { get; set; } = new List<ReceiptLine>();

    /// <summary>Quantity still outstanding on this line.</summary>
    public decimal OutstandingQuantity => Math.Max(0m, ExpectedQuantity - ReceivedQuantity);

    public bool IsFullyReceived => ReceivedQuantity >= ExpectedQuantity;
}
