using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Inventory;

/// <summary>
/// Doc §5.3 - batch-tracked stock. Two lots of the same item with different expiration
/// dates must remain separate inventory records even before a FEFO algorithm exists
/// (acceptance scenario 4).
/// </summary>
public class Lot : AuditableEntity
{
    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public string LotNumber { get; set; } = null!;

    public DateTimeOffset? ManufactureDate { get; set; }
    public DateTimeOffset? ReceivedDate { get; set; }

    /// <summary>
    /// Doc §11.6 - mandatory when the item is expiration-tracked. Enforced in the
    /// application layer where the item's flags are available.
    /// </summary>
    public DateTimeOffset? ExpirationDate { get; set; }

    public string? SupplierLotNumber { get; set; }

    public LotStatus Status { get; set; } = LotStatus.Active;

    public ICollection<InventoryBalance> Balances { get; set; } = new List<InventoryBalance>();

    /// <summary>True when the lot has an expiration date that has already passed.</summary>
    public bool IsExpired(DateTimeOffset asOf) => ExpirationDate.HasValue && ExpirationDate.Value <= asOf;

    /// <summary>Remaining shelf life in days, used for OrderDetail.MinimumShelfLifeDays checks (§7.2).</summary>
    public int? RemainingShelfLifeDays(DateTimeOffset asOf)
        => ExpirationDate.HasValue ? (int)Math.Floor((ExpirationDate.Value - asOf).TotalDays) : null;
}
