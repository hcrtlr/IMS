using IMS.Domain.Common;
using IMS.Domain.Enums;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;

namespace IMS.Domain.Entities.Outbound;

/// <summary>
/// Doc §7.1 - a customer order. Lifecycle: Draft -> Created -> Released ->
/// PartiallyAllocated -> Allocated -> Picking -> Picked -> Packed -> Shipped -> Cancelled.
///
/// The header also carries every §10 "Order uzerinde" field needed by future order
/// batching and picking algorithms.
/// </summary>
public class OrderMaster : AuditableEntity, IAccountScoped, IWarehouseScoped
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public string OrderNumber { get; set; } = null!;

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // --- Doc §10 "Order uzerinde": inputs for future batching / routing. ---
    public DateTimeOffset OrderDate { get; set; }
    public DateTimeOffset? RequiredShipDate { get; set; }
    public string? Carrier { get; set; }
    public string? ServiceLevel { get; set; }

    /// <summary>Lower number = higher urgency.</summary>
    public int Priority { get; set; } = 100;

    public OrderType OrderType { get; set; } = OrderType.Standard;
    public OrderStatus Status { get; set; } = OrderStatus.Draft;

    /// <summary>Denormalised totals, recomputed from lines. Doc §10 requires them for batching.</summary>
    public decimal? TotalWeight { get; set; }
    public decimal? TotalVolume { get; set; }

    /// <summary>Line count and unit count, also named by §10.</summary>
    public int TotalLineCount { get; set; }
    public decimal TotalQuantity { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public string? Notes { get; set; }

    public ICollection<OrderDetail> Details { get; set; } = new List<OrderDetail>();
    public ICollection<PickTask> PickTasks { get; set; } = new List<PickTask>();

    private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
    {
        [OrderStatus.Draft] = [OrderStatus.Created, OrderStatus.Cancelled],
        [OrderStatus.Created] = [OrderStatus.Released, OrderStatus.Cancelled],
        [OrderStatus.Released] = [OrderStatus.PartiallyAllocated, OrderStatus.Allocated, OrderStatus.Cancelled],
        [OrderStatus.PartiallyAllocated] = [OrderStatus.PartiallyAllocated, OrderStatus.Allocated, OrderStatus.Picking, OrderStatus.Cancelled],
        [OrderStatus.Allocated] = [OrderStatus.Picking, OrderStatus.Cancelled],
        [OrderStatus.Picking] = [OrderStatus.Picking, OrderStatus.Picked, OrderStatus.Cancelled],
        [OrderStatus.Picked] = [OrderStatus.Packed, OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Packed] = [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [],
        [OrderStatus.Cancelled] = []
    };

    public bool CanTransitionTo(OrderStatus target)
        => Allowed.TryGetValue(Status, out var next) && next.Contains(target);

    public void TransitionTo(OrderStatus target)
    {
        if (!CanTransitionTo(target))
            throw new InvalidStateTransitionException(nameof(OrderMaster), Status.ToString(), target.ToString());

        Status = target;
    }

    /// <summary>
    /// Derives the header status from line allocation progress. Acceptance scenario 3
    /// depends on this never reaching Allocated when any line is short.
    /// </summary>
    public void RecalculateAllocationStatus()
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Shipped)
            return;

        var lines = Details.Where(d => d.Status != OrderDetailStatus.Cancelled).ToList();
        if (lines.Count == 0) return;

        var fullyAllocated = lines.All(d => d.AllocatedQuantity >= d.OrderedQuantity);
        var anyAllocated = lines.Any(d => d.AllocatedQuantity > 0);

        Status = fullyAllocated
            ? OrderStatus.Allocated
            : anyAllocated
                ? OrderStatus.PartiallyAllocated
                : OrderStatus.Released;
    }

    /// <summary>Derives the header status from line picking progress.</summary>
    public void RecalculatePickStatus()
    {
        if (Status is OrderStatus.Cancelled or OrderStatus.Shipped) return;

        var lines = Details.Where(d => d.Status != OrderDetailStatus.Cancelled).ToList();
        if (lines.Count == 0) return;

        var allPicked = lines.All(d => d.PickedQuantity >= d.AllocatedQuantity && d.AllocatedQuantity > 0);

        Status = allPicked ? OrderStatus.Picked : OrderStatus.Picking;
    }

    /// <summary>Recomputes the §10 header totals from the current lines.</summary>
    public void RecalculateTotals()
    {
        var lines = Details.Where(d => d.Status != OrderDetailStatus.Cancelled).ToList();

        TotalLineCount = lines.Count;
        TotalQuantity = lines.Sum(d => d.OrderedQuantity);
        TotalWeight = lines.Sum(d => (d.Item?.Weight ?? 0m) * d.OrderedQuantity);
        TotalVolume = lines.Sum(d => (d.Item?.Volume ?? 0m) * d.OrderedQuantity);
    }
}
