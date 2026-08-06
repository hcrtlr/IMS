using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §4.3 - per-item unit-of-measure conversions and packaging hierarchy.
/// Example: 1 Each = 1, 1 Pack = 6, 1 Case = 24, 1 Pallet = 40 cases.
///
/// ConversionQuantity expresses how many BASE units one of this UOM contains, so all
/// inventory maths can normalise to the item's base UOM.
/// </summary>
public class ItemUom : AuditableEntity
{
    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    /// <summary>
    /// FK to the UOM master. Not in the doc's §4.3 field list, but the row is meaningless
    /// without it. ASSUMPTION - see docs/ASSUMPTIONS.md.
    /// </summary>
    public Guid UomId { get; set; }
    public UnitOfMeasure Uom { get; set; } = null!;

    /// <summary>Number of base units in one unit of this UOM. Must be greater than zero.</summary>
    public decimal ConversionQuantity { get; set; } = 1m;

    /// <summary>Doc §4.3 lists Barcode here as well as on ItemBarcode; kept for parity.</summary>
    public string? Barcode { get; set; }

    // Packaging dimensions at this UOM level - feed cube/capacity checks (rule §11.7).
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? Weight { get; set; }

    public bool IsReceivingUom { get; set; }
    public bool IsPickingUom { get; set; }
    public bool IsShippingUom { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Converts a quantity expressed in this UOM into the item's base UOM.</summary>
    public decimal ToBaseQuantity(decimal quantityInThisUom) => quantityInThisUom * ConversionQuantity;

    /// <summary>Converts a base-UOM quantity into this UOM.</summary>
    public decimal FromBaseQuantity(decimal baseQuantity)
        => ConversionQuantity == 0m ? 0m : baseQuantity / ConversionQuantity;
}
