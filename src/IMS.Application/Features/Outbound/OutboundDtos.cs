using IMS.Application.Common.Models;
using IMS.Domain.Enums;

namespace IMS.Application.Features.Outbound;

// ---------------------------------------------------------------------------
// Doc §7.1 / §7.2 - order master and detail
// ---------------------------------------------------------------------------

public sealed record OrderDto(
    Guid Id, Guid AccountId, Guid WarehouseId,
    string OrderNumber,
    Guid? CustomerId, string? CustomerName,
    DateTimeOffset OrderDate, DateTimeOffset? RequiredShipDate,
    string? Carrier, string? ServiceLevel, int Priority,
    OrderType OrderType, OrderStatus Status,
    decimal? TotalWeight, decimal? TotalVolume,
    int TotalLineCount, decimal TotalQuantity,
    DateTimeOffset? ReleasedAt, DateTimeOffset? ShippedAt,
    string? Notes, DateTimeOffset CreatedAt,
    IReadOnlyList<OrderDetailDto> Details);

public sealed record OrderDetailDto(
    Guid Id, int LineNumber,
    Guid ItemId, string Sku, string ItemName,
    decimal OrderedQuantity, decimal AllocatedQuantity,
    decimal PickedQuantity, decimal ShippedQuantity,
    decimal UnallocatedQuantity,
    Guid UomId, string UomCode,
    string? RequiredLotNumber, string? RequiredSerialNumber, int? MinimumShelfLifeDays,
    OrderDetailStatus Status,
    IReadOnlyList<AllocationDto> Allocations);

public sealed record OrderSummaryDto(
    Guid Id, string OrderNumber, Guid WarehouseId,
    string? CustomerName, OrderType OrderType, OrderStatus Status,
    int Priority, DateTimeOffset OrderDate, DateTimeOffset? RequiredShipDate,
    int TotalLineCount, decimal TotalQuantity);

public sealed record CreateOrderRequest(
    Guid WarehouseId,
    string? OrderNumber,
    Guid? CustomerId,
    DateTimeOffset? OrderDate,
    DateTimeOffset? RequiredShipDate,
    string? Carrier,
    string? ServiceLevel,
    int? Priority,
    OrderType? OrderType,
    string? Notes,
    IReadOnlyList<CreateOrderLineRequest> Lines);

public sealed record CreateOrderLineRequest(
    Guid ItemId,
    decimal OrderedQuantity,
    Guid UomId,
    string? RequiredLotNumber,
    string? RequiredSerialNumber,
    int? MinimumShelfLifeDays);

// ---------------------------------------------------------------------------
// Doc §7.3 - allocation
// ---------------------------------------------------------------------------

public sealed record AllocationDto(
    Guid Id, Guid OrderDetailId,
    Guid InventoryBalanceId,
    Guid LocationId, string LocationCode,
    Guid ItemId, string Sku,
    Guid? LotId, string? LotNumber, DateTimeOffset? ExpirationDate,
    Guid? SerialId, string? SerialNumber,
    Guid? LicensePlateId,
    decimal AllocatedQuantity, decimal PickedQuantity,
    AllocationStrategy AllocationStrategy,
    AllocationStatus Status);

/// <summary>POST /api/orders/{id}/allocate (§12).</summary>
public sealed record AllocateRequest(
    /// <summary>Restrict allocation to specific lines; null allocates every open line.</summary>
    IReadOnlyList<Guid>? OrderDetailIds,
    /// <summary>
    /// When true (the default), a line that cannot be fully covered is still partially
    /// allocated and the shortfall reported. When false the whole request is rejected
    /// unless every line can be satisfied in full.
    /// </summary>
    bool AllowPartial = true);

/// <summary>
/// Result of an allocation run. Acceptance scenario 3 requires reporting which item is
/// short and by how much, without the order becoming fully allocated.
/// </summary>
public sealed record AllocationResultDto(
    Guid OrderId,
    OrderStatus Status,
    bool IsFullyAllocated,
    IReadOnlyList<AllocationDto> Allocations,
    IReadOnlyList<ShortfallDto> Shortfalls);

public sealed record ShortfallDto(
    Guid OrderDetailId, int LineNumber,
    Guid ItemId, string Sku,
    decimal RequestedQuantity, decimal AllocatedQuantity, decimal ShortQuantity);

// ---------------------------------------------------------------------------
// Doc §7.4 - pick task
// ---------------------------------------------------------------------------

/// <summary>POST /api/orders/{id}/create-pick-tasks (§12).</summary>
public sealed record CreatePickTasksRequest(
    /// <summary>Where picked stock is dropped; typically a packing or staging location.</summary>
    Guid? DestinationLocationId,
    string? AssignTo);

/// <summary>POST /api/pick-tasks/{id}/complete (§12).</summary>
public sealed record CompletePickRequest(
    /// <summary>Null picks the full task quantity. Less than the task quantity is a short pick.</summary>
    decimal? PickedQuantity,
    string? Notes);

public sealed record PickTaskDto(
    Guid Id, Guid WarehouseId,
    Guid OrderId, string OrderNumber,
    Guid OrderDetailId, Guid AllocationId,
    Guid ItemId, string Sku, string ItemName,
    Guid FromLocationId, string FromLocationCode,
    Guid? DestinationLocationId, string? DestinationLocationCode,
    decimal Quantity, decimal PickedQuantity, decimal ShortQuantity,
    int SequenceNumber, Guid? PickBatchId,
    PickTaskStatus Status,
    string? AssignedTo,
    string? LotNumber, string? SerialNumber,
    DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? Notes);

// ---------------------------------------------------------------------------
// Shipment
// ---------------------------------------------------------------------------

/// <summary>POST /api/orders/{id}/ship (§12).</summary>
public sealed record ShipRequest(
    string? Carrier,
    string? ServiceLevel,
    string? TrackingNumber,
    string? Notes);

public sealed record ShipmentDto(
    Guid Id, string ShipmentNumber,
    Guid OrderId, string OrderNumber,
    string? Carrier, string? ServiceLevel, string? TrackingNumber,
    DateTimeOffset ShippedAt, string? ShippedBy,
    decimal? TotalWeight, decimal? TotalVolume,
    Guid CorrelationId,
    IReadOnlyList<ShipmentLineDto> Lines);

public sealed record ShipmentLineDto(
    Guid Id, Guid OrderDetailId,
    Guid ItemId, string Sku,
    Guid? LotId, string? LotNumber,
    Guid? SerialId,
    decimal Quantity);

public sealed class OrderQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? CustomerId { get; set; }
    public OrderStatus? Status { get; set; }
    public OrderType? OrderType { get; set; }
}

public sealed class PickTaskQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? OrderId { get; set; }
    public PickTaskStatus? Status { get; set; }
    public string? AssignedTo { get; set; }
}
