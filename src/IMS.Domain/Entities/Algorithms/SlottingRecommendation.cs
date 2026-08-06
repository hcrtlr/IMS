using IMS.Domain.Common;
using IMS.Domain.Entities.MasterData;

namespace IMS.Domain.Entities.Algorithms;

/// <summary>
/// Faz 6 "Slotting recommendation kayitlari" - where a future slotting engine would
/// suggest an item be stored, and whether the suggestion was accepted. Nothing writes
/// to this in v1; the table exists so recommendation quality can be measured later.
/// </summary>
public class SlottingRecommendation : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    /// <summary>Where the item currently sits, if anywhere.</summary>
    public Guid? CurrentLocationId { get; set; }
    public Location? CurrentLocation { get; set; }

    /// <summary>Where the engine proposes it should sit.</summary>
    public Guid RecommendedLocationId { get; set; }
    public Location RecommendedLocation { get; set; } = null!;

    /// <summary>Confidence or fitness of the recommendation, 0-100.</summary>
    public decimal? Score { get; set; }

    /// <summary>Human-readable justification, mirroring PutawayTask.RecommendationReason (§6.4).</summary>
    public string? RecommendationReason { get; set; }

    /// <summary>Which algorithm and version produced this, for A/B comparison.</summary>
    public string? AlgorithmName { get; set; }
    public string? AlgorithmVersion { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Null while undecided; true/false once an operator acts on it.</summary>
    public bool? WasAccepted { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
}
