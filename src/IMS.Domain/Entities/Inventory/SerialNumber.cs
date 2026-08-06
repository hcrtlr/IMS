using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Inventory;

/// <summary>
/// Doc §5.4 - individually tracked stock. Rule §11.5: the quantity of a serial-tracked
/// stock record must never exceed 1.
/// </summary>
public class SerialNumber : AuditableEntity
{
    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public string Serial { get; set; } = null!;

    /// <summary>A serial may belong to a lot when the item is both lot- and serial-tracked.</summary>
    public Guid? LotId { get; set; }
    public Lot? Lot { get; set; }

    public SerialStatus Status { get; set; } = SerialStatus.Available;
}
