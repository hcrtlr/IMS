using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;

namespace IMS.Domain.Entities.Inbound;

/// <summary>
/// Doc §6.4 - moves received stock from the receiving area to a storage or picking
/// location. In v1 the target location is chosen by the user; later a slotting/putaway
/// algorithm will populate SuggestedLocationId and RecommendationReason.
/// </summary>
public class PutawayTask : AuditableEntity, IWarehouseScoped
{
    public Guid WarehouseId { get; set; }

    public Guid ReceiptLineId { get; set; }
    public ReceiptLine ReceiptLine { get; set; } = null!;

    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid FromLocationId { get; set; }
    public Location FromLocation { get; set; } = null!;

    /// <summary>Algorithm-proposed destination. Null in v1 - reserved for the future slotting engine.</summary>
    public Guid? SuggestedLocationId { get; set; }
    public Location? SuggestedLocation { get; set; }

    /// <summary>Where the stock actually went; set on completion.</summary>
    public Guid? ActualLocationId { get; set; }
    public Location? ActualLocation { get; set; }

    /// <summary>Quantity to put away, in the item's base UOM.</summary>
    public decimal Quantity { get; set; }

    public PutawayTaskStatus Status { get; set; } = PutawayTaskStatus.Created;

    /// <summary>
    /// Doc §6.4 - why the algorithm suggested SuggestedLocationId. Left null while
    /// putaway is manual, so the field is ready when the algorithm arrives.
    /// </summary>
    public string? RecommendationReason { get; set; }

    public string? AssignedTo { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    private static readonly Dictionary<PutawayTaskStatus, PutawayTaskStatus[]> Allowed = new()
    {
        [PutawayTaskStatus.Created] = [PutawayTaskStatus.Assigned, PutawayTaskStatus.InProgress, PutawayTaskStatus.Completed, PutawayTaskStatus.Cancelled],
        [PutawayTaskStatus.Assigned] = [PutawayTaskStatus.InProgress, PutawayTaskStatus.Completed, PutawayTaskStatus.Cancelled],
        [PutawayTaskStatus.InProgress] = [PutawayTaskStatus.Completed, PutawayTaskStatus.Cancelled],
        [PutawayTaskStatus.Completed] = [],
        [PutawayTaskStatus.Cancelled] = []
    };

    public void TransitionTo(PutawayTaskStatus target)
    {
        if (!Allowed.TryGetValue(Status, out var next) || !next.Contains(target))
            throw new InvalidStateTransitionException(nameof(PutawayTask), Status.ToString(), target.ToString());

        Status = target;
    }
}
