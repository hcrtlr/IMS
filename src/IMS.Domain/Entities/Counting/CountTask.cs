using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;

namespace IMS.Domain.Entities.Counting;

/// <summary>
/// Backs POST /api/count-tasks/{id}/complete (§12). Designed by analogy with PickTask
/// (§7.4) because §9 is missing from the source document. See docs/ASSUMPTIONS.md.
///
/// One task = count one item at one location, in one status/lot/serial/LPN combination,
/// so a variance can be pinned to an exact InventoryBalance row.
/// </summary>
public class CountTask : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }

    public Guid CountPlanId { get; set; }
    public CountPlan CountPlan { get; set; } = null!;

    public Guid LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid InventoryStatusId { get; set; }
    public InventoryStatus InventoryStatus { get; set; } = null!;

    public Guid? LotId { get; set; }
    public Lot? Lot { get; set; }

    public Guid? SerialId { get; set; }
    public SerialNumber? Serial { get; set; }

    public Guid? LicensePlateId { get; set; }
    public LicensePlate? LicensePlate { get; set; }

    /// <summary>
    /// The balance row being counted. Null when the counter finds stock the system did
    /// not know about, which produces a positive adjustment.
    /// </summary>
    public Guid? InventoryBalanceId { get; set; }
    public InventoryBalance? InventoryBalance { get; set; }

    /// <summary>
    /// On-hand quantity snapshotted when the task was generated. Kept blind from the
    /// counter in the UI so the count is not anchored.
    /// </summary>
    public decimal SystemQuantity { get; set; }

    /// <summary>What the counter actually found. Null until the task is completed.</summary>
    public decimal? CountedQuantity { get; set; }

    public CountTaskStatus Status { get; set; } = CountTaskStatus.Created;

    public string? AssignedTo { get; set; }
    public DateTimeOffset? CountedAt { get; set; }
    public string? CountedBy { get; set; }
    public string? Notes { get; set; }

    /// <summary>Adjustment raised when the count differed from the system quantity.</summary>
    public Guid? InventoryAdjustmentId { get; set; }
    public InventoryAdjustment? InventoryAdjustment { get; set; }

    /// <summary>Counted minus system. Positive means a surplus was found.</summary>
    public decimal? Variance => CountedQuantity.HasValue ? CountedQuantity.Value - SystemQuantity : null;

    public bool HasVariance => Variance.HasValue && Variance.Value != 0m;

    private static readonly Dictionary<CountTaskStatus, CountTaskStatus[]> Allowed = new()
    {
        [CountTaskStatus.Created] = [CountTaskStatus.Assigned, CountTaskStatus.InProgress, CountTaskStatus.Counted, CountTaskStatus.VarianceFound, CountTaskStatus.Cancelled],
        [CountTaskStatus.Assigned] = [CountTaskStatus.InProgress, CountTaskStatus.Counted, CountTaskStatus.VarianceFound, CountTaskStatus.Cancelled],
        [CountTaskStatus.InProgress] = [CountTaskStatus.Counted, CountTaskStatus.VarianceFound, CountTaskStatus.Cancelled],
        // A variance stays open until its adjustment is approved or rejected.
        [CountTaskStatus.VarianceFound] = [CountTaskStatus.Completed, CountTaskStatus.Cancelled],
        [CountTaskStatus.Counted] = [CountTaskStatus.Completed],
        [CountTaskStatus.Completed] = [],
        [CountTaskStatus.Cancelled] = []
    };

    public void TransitionTo(CountTaskStatus target)
    {
        if (!Allowed.TryGetValue(Status, out var next) || !next.Contains(target))
            throw new InvalidStateTransitionException(nameof(CountTask), Status.ToString(), target.ToString());

        Status = target;
    }
}
