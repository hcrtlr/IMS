using IMS.Domain.Common;
using IMS.Domain.Enums;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §3.5 - shared rule-set for locations with similar characteristics.
/// Supplies the constraints enforced by business rules §11.7 (capacity) and
/// §11.8 (temperature / hazardous-material compatibility).
/// </summary>
public class LocationProfile : AuditableEntity, IAccountScoped
{
    /// <summary>Not listed in §3.5 but required to keep profiles tenant-isolated. ASSUMPTION.</summary>
    public Guid AccountId { get; set; }

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public LocationType LocationType { get; set; }

    public decimal? MaxWeight { get; set; }
    public decimal? MaxVolume { get; set; }

    /// <summary>
    /// Doc §3.5 "AllowedItemCategory". Null means every category is allowed.
    /// Modelled as an FK to ItemCategory rather than free text.
    /// </summary>
    public Guid? AllowedItemCategoryId { get; set; }
    public ItemCategory? AllowedItemCategory { get; set; }

    public decimal? TemperatureMin { get; set; }
    public decimal? TemperatureMax { get; set; }

    /// <summary>Whether more than one distinct item may share a location using this profile.</summary>
    public bool IsMixedItemAllowed { get; set; } = true;

    /// <summary>Whether more than one lot of the same item may share a location using this profile.</summary>
    public bool IsMixedLotAllowed { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public ICollection<Location> Locations { get; set; } = new List<Location>();

    /// <summary>
    /// True when this profile can hold an item needing the given temperature band.
    /// Backs business rule §11.8.
    /// </summary>
    public bool SupportsTemperatureRange(decimal? itemMin, decimal? itemMax)
    {
        if (itemMin is null && itemMax is null) return true;

        // A profile with no declared band is ambient-only and cannot guarantee a controlled range.
        if (TemperatureMin is null && TemperatureMax is null) return false;

        if (itemMin is not null && TemperatureMin is not null && itemMin < TemperatureMin) return false;
        if (itemMax is not null && TemperatureMax is not null && itemMax > TemperatureMax) return false;

        return true;
    }
}
