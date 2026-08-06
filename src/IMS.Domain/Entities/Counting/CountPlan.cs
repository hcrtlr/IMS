using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;

namespace IMS.Domain.Entities.Counting;

/// <summary>
/// Backs POST /api/count-plans (§12) and Faz 5. Section 9 of the source document, which
/// would have specified counting, is MISSING - this entity is designed by analogy with
/// the documented inbound order + task pattern. See docs/ASSUMPTIONS.md.
///
/// A plan defines WHAT to count; releasing it generates one CountTask per location/item
/// combination in scope.
/// </summary>
public class CountPlan : AuditableEntity, IWarehouseScoped, IAccountScoped
{
    public Guid AccountId { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string PlanNumber { get; set; } = null!;
    public string? Name { get; set; }

    public CountType CountType { get; set; } = CountType.Cycle;
    public CountSelectionMode SelectionMode { get; set; } = CountSelectionMode.ByLocation;

    /// <summary>Populated when SelectionMode is ByZone.</summary>
    public Guid? ZoneId { get; set; }
    public Zone? Zone { get; set; }

    /// <summary>Populated when SelectionMode is ByItem.</summary>
    public Guid? ItemId { get; set; }
    public ItemMaster? Item { get; set; }

    public CountPlanStatus Status { get; set; } = CountPlanStatus.Draft;

    public DateTimeOffset? ScheduledDate { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// When true, counted stock is frozen (blocked from allocation) until the count
    /// is resolved. Kept false by default so counting never disrupts outbound in v1.
    /// </summary>
    public bool BlockAllocationDuringCount { get; set; }

    public string? Notes { get; set; }

    public ICollection<CountTask> Tasks { get; set; } = new List<CountTask>();

    private static readonly Dictionary<CountPlanStatus, CountPlanStatus[]> Allowed = new()
    {
        [CountPlanStatus.Draft] = [CountPlanStatus.Released, CountPlanStatus.Cancelled],
        [CountPlanStatus.Released] = [CountPlanStatus.InProgress, CountPlanStatus.Completed, CountPlanStatus.Cancelled],
        [CountPlanStatus.InProgress] = [CountPlanStatus.InProgress, CountPlanStatus.Completed, CountPlanStatus.Cancelled],
        [CountPlanStatus.Completed] = [],
        [CountPlanStatus.Cancelled] = []
    };

    public void TransitionTo(CountPlanStatus target)
    {
        if (!Allowed.TryGetValue(Status, out var next) || !next.Contains(target))
            throw new InvalidStateTransitionException(nameof(CountPlan), Status.ToString(), target.ToString());

        Status = target;
    }

    /// <summary>Derives plan status from the progress of its tasks.</summary>
    public void RecalculateStatus()
    {
        if (Status is CountPlanStatus.Cancelled or CountPlanStatus.Draft) return;

        var open = Tasks.Where(t => t.Status != CountTaskStatus.Cancelled).ToList();
        if (open.Count == 0) return;

        var allDone = open.All(t => t.Status is CountTaskStatus.Completed or CountTaskStatus.Counted);
        var anyStarted = open.Any(t => t.Status != CountTaskStatus.Created);

        Status = allDone ? CountPlanStatus.Completed
            : anyStarted ? CountPlanStatus.InProgress
            : CountPlanStatus.Released;
    }
}
