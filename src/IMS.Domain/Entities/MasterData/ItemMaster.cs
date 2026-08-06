using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §4.1 - the core product record. Carries physical, operational and logistics
/// attributes, not just name and SKU. SKU is unique within an Account.
/// </summary>
public class ItemMaster : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    /// <summary>Stock Keeping Unit. Unique per account (doc §4.1).</summary>
    public string Sku { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }
    public ItemCategory? Category { get; set; }

    /// <summary>The unit every inventory quantity for this item is expressed in.</summary>
    public Guid BaseUomId { get; set; }
    public UnitOfMeasure BaseUom { get; set; } = null!;

    // --- Physical characteristics (doc §4.1, also required by §10 for slotting). ---
    public decimal? Weight { get; set; }
    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }
    public decimal? Volume { get; set; }

    // --- Tracking flags. Drive lot/serial/expiration capture and rules §11.5 and §11.6. ---
    public bool IsLotTracked { get; set; }
    public bool IsSerialTracked { get; set; }
    public bool IsExpirationTracked { get; set; }

    /// <summary>Shelf life in days, used to derive an expiration date when not supplied.</summary>
    public int? ShelfLifeDays { get; set; }

    // --- Handling constraints. Feed putaway compatibility rule §11.8. ---
    public bool IsFragile { get; set; }
    public bool IsHazardous { get; set; }
    public bool IsTemperatureControlled { get; set; }
    public decimal? MinimumStorageTemperature { get; set; }
    public decimal? MaximumStorageTemperature { get; set; }

    /// <summary>How many units may be stacked on top of each other.</summary>
    public int? StackableQuantity { get; set; }

    /// <summary>Preferred zone for putaway; a hint for the future slotting algorithm.</summary>
    public Guid? DefaultPutawayZoneId { get; set; }
    public Zone? DefaultPutawayZone { get; set; }

    /// <summary>Preferred zone for picking; a hint for the future picking algorithm.</summary>
    public Guid? DefaultPickZoneId { get; set; }
    public Zone? DefaultPickZone { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ItemAttributeValue> AttributeValues { get; set; } = new List<ItemAttributeValue>();
    public ICollection<ItemUom> ItemUoms { get; set; } = new List<ItemUom>();
    public ICollection<ItemBarcode> Barcodes { get; set; } = new List<ItemBarcode>();
}
