using IMS.Domain.Common;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Entities.Outbound;

namespace IMS.Domain.Entities.Algorithms;

/// <summary>
/// Faz 6 "Picking plan ve route kayitlari" - a batch of pick work plus the route a
/// future routing algorithm would walk. Not generated in v1; the schema exists so
/// planned-versus-actual travel can be compared once an algorithm is written.
/// </summary>
public class PickingPlan : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }

    public string PlanNumber { get; set; } = null!;

    /// <summary>Which algorithm and version produced this plan.</summary>
    public string? AlgorithmName { get; set; }
    public string? AlgorithmVersion { get; set; }

    /// <summary>How the work was grouped, e.g. "SingleOrder", "Batch", "Zone", "Wave".</summary>
    public string? BatchingStrategy { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public int TotalStops { get; set; }
    public decimal? EstimatedTravelDistance { get; set; }
    public decimal? ActualTravelDistance { get; set; }
    public int? EstimatedDurationSeconds { get; set; }
    public int? ActualDurationSeconds { get; set; }

    public string? AssignedTo { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<PickingRouteStop> Stops { get; set; } = new List<PickingRouteStop>();
}

/// <summary>One stop on a picking route, in planned traversal order.</summary>
public class PickingRouteStop : AuditableEntity
{
    public Guid PickingPlanId { get; set; }
    public PickingPlan PickingPlan { get; set; } = null!;

    /// <summary>Position in the route, starting at 1.</summary>
    public int StopSequence { get; set; }

    public Guid LocationId { get; set; }
    public Location Location { get; set; } = null!;

    /// <summary>The pick task served at this stop, when the plan is tied to real work.</summary>
    public Guid? PickTaskId { get; set; }
    public PickTask? PickTask { get; set; }

    public Guid? ItemId { get; set; }
    public decimal? Quantity { get; set; }

    /// <summary>Distance from the previous stop, in metres.</summary>
    public decimal? DistanceFromPrevious { get; set; }

    public DateTimeOffset? ArrivedAt { get; set; }
    public DateTimeOffset? DepartedAt { get; set; }
}
