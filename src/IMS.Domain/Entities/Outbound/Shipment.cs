using IMS.Domain.Common;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Outbound;

/// <summary>
/// The shipping event behind POST /api/orders/{id}/ship (§12). Faz 4 lists "Shipment"
/// as a deliverable but the doc defines no fields; modelled minimally as the record of
/// what physically left the building. ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class Shipment : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }

    public Guid OrderId { get; set; }
    public OrderMaster Order { get; set; } = null!;

    public string ShipmentNumber { get; set; } = null!;

    public string? Carrier { get; set; }
    public string? ServiceLevel { get; set; }
    public string? TrackingNumber { get; set; }

    public DateTimeOffset ShippedAt { get; set; }
    public string? ShippedBy { get; set; }

    public decimal? TotalWeight { get; set; }
    public decimal? TotalVolume { get; set; }

    /// <summary>Correlates every InventoryTransaction written by this shipment.</summary>
    public Guid CorrelationId { get; set; }

    public ICollection<ShipmentLine> Lines { get; set; } = new List<ShipmentLine>();
}

/// <summary>One shipped order line, capturing exactly which stock left.</summary>
public class ShipmentLine : AuditableEntity
{
    public Guid ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    public Guid OrderDetailId { get; set; }
    public OrderDetail OrderDetail { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid? LotId { get; set; }
    public Guid? SerialId { get; set; }
    public Guid? LicensePlateId { get; set; }

    /// <summary>Shipped quantity in base UOM.</summary>
    public decimal Quantity { get; set; }
}
