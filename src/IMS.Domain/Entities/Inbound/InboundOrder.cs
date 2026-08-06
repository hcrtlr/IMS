using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;

namespace IMS.Domain.Entities.Inbound;

/// <summary>
/// Doc §6.1 - an expected goods receipt.
/// Lifecycle: Draft -> Expected -> PartiallyReceived -> Received -> Completed -> Cancelled.
/// </summary>
public class InboundOrder : AuditableEntity, IWarehouseScoped, IAccountScoped
{
    /// <summary>Not in §6.1's field list; added for tenant scoping per §3. ASSUMPTION.</summary>
    public Guid AccountId { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string OrderNumber { get; set; } = null!;

    public Guid? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public DateTimeOffset? ExpectedArrivalDate { get; set; }

    public InboundOrderStatus Status { get; set; } = InboundOrderStatus.Draft;

    public string? Notes { get; set; }

    public ICollection<InboundOrderDetail> Details { get; set; } = new List<InboundOrderDetail>();
    public ICollection<Receipt> Receipts { get; set; } = new List<Receipt>();

    private static readonly Dictionary<InboundOrderStatus, InboundOrderStatus[]> Allowed = new()
    {
        [InboundOrderStatus.Draft] = [InboundOrderStatus.Expected, InboundOrderStatus.Cancelled],
        [InboundOrderStatus.Expected] = [InboundOrderStatus.PartiallyReceived, InboundOrderStatus.Received, InboundOrderStatus.Cancelled],
        [InboundOrderStatus.PartiallyReceived] = [InboundOrderStatus.PartiallyReceived, InboundOrderStatus.Received, InboundOrderStatus.Cancelled],
        [InboundOrderStatus.Received] = [InboundOrderStatus.Completed],
        [InboundOrderStatus.Completed] = [],
        [InboundOrderStatus.Cancelled] = []
    };

    public bool CanTransitionTo(InboundOrderStatus target)
        => Allowed.TryGetValue(Status, out var next) && next.Contains(target);

    public void TransitionTo(InboundOrderStatus target)
    {
        if (!CanTransitionTo(target))
            throw new InvalidStateTransitionException(nameof(InboundOrder), Status.ToString(), target.ToString());

        Status = target;
    }

    /// <summary>
    /// Recomputes the header status from line receipt progress, called after each receipt.
    /// </summary>
    public void RecalculateStatus()
    {
        if (Status is InboundOrderStatus.Cancelled or InboundOrderStatus.Completed)
            return;

        var anyReceived = Details.Any(d => d.ReceivedQuantity > 0);
        var allReceived = Details.Count > 0 && Details.All(d => d.ReceivedQuantity >= d.ExpectedQuantity);

        Status = allReceived
            ? InboundOrderStatus.Received
            : anyReceived
                ? InboundOrderStatus.PartiallyReceived
                : Status;
    }
}
