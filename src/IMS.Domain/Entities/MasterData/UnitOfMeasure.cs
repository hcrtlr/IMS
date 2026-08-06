using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// The UOM master referenced by ItemMaster.BaseUomId (§4.1), ItemBarcode.UomId (§4.4)
/// and the order/inbound detail lines. The document names these foreign keys and gives
/// Each / Pack / Case / Pallet as examples (§4.3) but never defines the table itself.
/// ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class UnitOfMeasure : AuditableEntity
{
    /// <summary>Short code, e.g. EA, PK, CS, PL.</summary>
    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
