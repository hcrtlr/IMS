using IMS.Domain.Common;
using IMS.Domain.Enums;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §3.4 - the smallest physical warehouse address, e.g. "A-03-B-02"
/// (Aisle A, Bay 03, Level B, Position 02).
///
/// Aisle is a field here rather than its own table: although §3 draws the hierarchy as
/// Zone -> Aisle -> Location, §3.4 lists Aisle as a plain Location column and defines no
/// Aisle entity. ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class Location : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid ZoneId { get; set; }
    public Zone Zone { get; set; } = null!;

    public string Code { get; set; } = null!;

    public string? Aisle { get; set; }
    public string? Bay { get; set; }
    public string? Level { get; set; }
    public string? Position { get; set; }

    public LocationType LocationType { get; set; }

    public Guid? LocationProfileId { get; set; }
    public LocationProfile? LocationProfile { get; set; }

    /// <summary>Traversal order for picking. Doc §10 - required for future picking routing.</summary>
    public int? PickSequence { get; set; }

    /// <summary>Traversal order for putaway. Doc §10 - required for future slotting.</summary>
    public int? PutawaySequence { get; set; }

    // --- Doc §10: coordinates. Unused in v1, mandatory for later routing algorithms. ---
    public decimal? CoordinateX { get; set; }
    public decimal? CoordinateY { get; set; }
    public decimal? CoordinateZ { get; set; }

    /// <summary>Capacity ceiling; null falls back to the location profile. Backs rule §11.7.</summary>
    public decimal? MaxWeight { get; set; }
    public decimal? MaxVolume { get; set; }

    public bool IsPickable { get; set; } = true;
    public bool IsPutawayAllowed { get; set; } = true;
    public bool IsActive { get; set; } = true;

    // ------------------------------------------------------------------
    // Doc §10 "Algoritmalar icin bastan eklenmesi gereken alanlar" - Location.
    // Captured now so slotting / picking routing can be built later.
    // ------------------------------------------------------------------

    /// <summary>Approximate travel distance to the receiving area, in metres.</summary>
    public decimal? DistanceToReceiving { get; set; }

    /// <summary>Approximate travel distance to the packing area, in metres.</summary>
    public decimal? DistanceToPacking { get; set; }

    /// <summary>Approximate travel distance to the shipping area, in metres.</summary>
    public decimal? DistanceToShipping { get; set; }

    /// <summary>
    /// Relative ease of access, 0-100 (higher is easier - e.g. golden-zone pick faces
    /// score high, top-level pallet positions score low).
    /// </summary>
    public int? AccessibilityScore { get; set; }

    /// <summary>How many workers can operate at this location simultaneously.</summary>
    public int? MaxConcurrentWorkers { get; set; }

    /// <summary>
    /// Effective weight ceiling: the location's own value, else the profile's.
    /// </summary>
    public decimal? EffectiveMaxWeight => MaxWeight ?? LocationProfile?.MaxWeight;

    /// <summary>
    /// Effective volume ceiling: the location's own value, else the profile's.
    /// </summary>
    public decimal? EffectiveMaxVolume => MaxVolume ?? LocationProfile?.MaxVolume;
}
