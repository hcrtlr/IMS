using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;

namespace IMS.Domain.Entities.Outbound;

/// <summary>
/// Doc §7.4 - the picking job a warehouse worker performs. On completion stock moves to
/// the pick or packing location; on shipment it leaves the system entirely.
/// </summary>
public class PickTask : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }

    public Guid OrderId { get; set; }
    public OrderMaster Order { get; set; } = null!;

    public Guid OrderDetailId { get; set; }
    public OrderDetail OrderDetail { get; set; } = null!;

    public Guid AllocationId { get; set; }
    public InventoryAllocation Allocation { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid FromLocationId { get; set; }
    public Location FromLocation { get; set; } = null!;

    /// <summary>Where the picked stock is dropped, typically a packing or staging location.</summary>
    public Guid? DestinationLocationId { get; set; }
    public Location? DestinationLocation { get; set; }

    /// <summary>Quantity to pick, in base UOM.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Quantity actually picked; less than Quantity means a short pick.</summary>
    public decimal PickedQuantity { get; set; }

    /// <summary>
    /// Travel order within a pick run, seeded from Location.PickSequence. The future
    /// routing algorithm will own this.
    /// </summary>
    public int SequenceNumber { get; set; }

    /// <summary>Groups tasks picked together. Reserved for the future order-batching algorithm.</summary>
    public Guid? PickBatchId { get; set; }

    public PickTaskStatus Status { get; set; } = PickTaskStatus.Created;

    public string? AssignedTo { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Notes { get; set; }

    private static readonly Dictionary<PickTaskStatus, PickTaskStatus[]> Allowed = new()
    {
        [PickTaskStatus.Created] = [PickTaskStatus.Assigned, PickTaskStatus.InProgress, PickTaskStatus.Completed, PickTaskStatus.ShortPicked, PickTaskStatus.Cancelled],
        [PickTaskStatus.Assigned] = [PickTaskStatus.InProgress, PickTaskStatus.Completed, PickTaskStatus.ShortPicked, PickTaskStatus.Cancelled],
        [PickTaskStatus.InProgress] = [PickTaskStatus.Completed, PickTaskStatus.ShortPicked, PickTaskStatus.Cancelled],
        [PickTaskStatus.Completed] = [],
        [PickTaskStatus.ShortPicked] = [],
        [PickTaskStatus.Cancelled] = []
    };

    public void TransitionTo(PickTaskStatus target)
    {
        if (!Allowed.TryGetValue(Status, out var next) || !next.Contains(target))
            throw new InvalidStateTransitionException(nameof(PickTask), Status.ToString(), target.ToString());

        Status = target;
    }

    /// <summary>Quantity requested but not picked.</summary>
    public decimal ShortQuantity => Math.Max(0m, Quantity - PickedQuantity);
}
