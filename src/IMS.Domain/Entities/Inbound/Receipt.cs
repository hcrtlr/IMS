using IMS.Domain.Common;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Inbound;

/// <summary>
/// Doc §6.3 - the physical intake event. During receipt the item is verified, quantity
/// entered, lot/serial captured, an LPN created if needed, stock added to the receiving
/// location and an InventoryTransaction written.
///
/// The doc describes the process but lists no fields; the header/line split mirrors the
/// inbound order structure. ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class Receipt : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid InboundOrderId { get; set; }
    public InboundOrder InboundOrder { get; set; } = null!;

    /// <summary>Human-readable receipt number, generated on creation.</summary>
    public string ReceiptNumber { get; set; } = null!;

    /// <summary>The receiving location the stock landed in (a Zone of type Receiving).</summary>
    public Guid ReceivingLocationId { get; set; }
    public Location ReceivingLocation { get; set; } = null!;

    public DateTimeOffset ReceivedAt { get; set; }
    public string? ReceivedBy { get; set; }
    public string? Notes { get; set; }

    /// <summary>Correlates every InventoryTransaction written by this receipt.</summary>
    public Guid CorrelationId { get; set; }

    public ICollection<ReceiptLine> Lines { get; set; } = new List<ReceiptLine>();
}
