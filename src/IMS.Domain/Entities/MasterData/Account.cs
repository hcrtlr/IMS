using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §3.1 - the company or customer using the system. Root of the
/// Account -> Warehouse -> Zone -> Aisle -> Location hierarchy.
/// </summary>
public class Account : AuditableEntity
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
    public ICollection<ItemMaster> Items { get; set; } = new List<ItemMaster>();
}
