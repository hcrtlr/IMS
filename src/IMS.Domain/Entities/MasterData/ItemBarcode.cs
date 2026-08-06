using IMS.Domain.Common;
using IMS.Domain.Enums;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §4.4 - an item may carry several barcodes, each tied to a UOM
/// (e.g. one barcode scans as an Each, another as a Case).
/// </summary>
public class ItemBarcode : AuditableEntity
{
    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid UomId { get; set; }
    public UnitOfMeasure Uom { get; set; } = null!;

    public string Barcode { get; set; } = null!;
    public BarcodeType BarcodeType { get; set; }

    /// <summary>At most one primary barcode per item.</summary>
    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;
}
