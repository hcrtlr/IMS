using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Algorithms;

/// <summary>
/// Faz 6 "Historical order verisi". Live order tables get purged or archived, but
/// slotting and batching algorithms need a stable demand history: how often an item is
/// ordered, in what quantities, alongside what. One row is written per shipped order
/// line, denormalised so it survives master-data changes.
/// </summary>
public class OrderHistorySnapshot : BaseEntity, IAccountScoped, IWarehouseScoped
{
    public Guid AccountId { get; set; }
    public Guid WarehouseId { get; set; }

    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = null!;
    public Guid OrderDetailId { get; set; }

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    /// <summary>Denormalised so history stays readable if the SKU is later renamed.</summary>
    public string Sku { get; set; } = null!;

    public Guid? CustomerId { get; set; }
    public OrderType OrderType { get; set; }
    public int Priority { get; set; }
    public string? Carrier { get; set; }
    public string? ServiceLevel { get; set; }

    public DateTimeOffset OrderDate { get; set; }
    public DateTimeOffset? RequiredShipDate { get; set; }
    public DateTimeOffset ShippedAt { get; set; }

    public decimal OrderedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }

    /// <summary>Lines on the parent order - the basis for affinity/co-pick analysis.</summary>
    public int OrderLineCount { get; set; }

    /// <summary>Location the stock was ultimately picked from, for travel analysis.</summary>
    public Guid? PickedFromLocationId { get; set; }

    /// <summary>Order-to-ship duration, for service-level analysis.</summary>
    public int? FulfillmentDurationSeconds { get; set; }
}
