using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Inventory;

/// <summary>
/// Doc §5.5 - a pallet, case, tote or other handling unit. Nesting is explicitly
/// preferred, e.g. Pallet -> Case 1 / Case 2 / Case 3.
/// </summary>
public class LicensePlate : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string Code { get; set; } = null!;

    /// <summary>Parent container in the nesting hierarchy; null for a top-level unit.</summary>
    public Guid? ParentLicensePlateId { get; set; }
    public LicensePlate? ParentLicensePlate { get; set; }
    public ICollection<LicensePlate> Children { get; set; } = new List<LicensePlate>();

    public LicensePlateType LicensePlateType { get; set; }

    public Guid? CurrentLocationId { get; set; }
    public Location? CurrentLocation { get; set; }

    public LicensePlateStatus Status { get; set; } = LicensePlateStatus.Open;

    public ICollection<InventoryBalance> Balances { get; set; } = new List<InventoryBalance>();
}
