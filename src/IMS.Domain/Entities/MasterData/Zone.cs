using IMS.Domain.Common;
using IMS.Domain.Enums;

namespace IMS.Domain.Entities.MasterData;

/// <summary>Doc §3.3 - a logical or physical area inside a warehouse.</summary>
public class Zone : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public ZoneType ZoneType { get; set; }

    /// <summary>Ordering hint used when several zones are candidates for the same operation.</summary>
    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Location> Locations { get; set; } = new List<Location>();
}
