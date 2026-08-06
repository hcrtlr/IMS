using IMS.Application.Common.Models;
using IMS.Domain.Enums;

namespace IMS.Application.Features.Inbound;

// ---------------------------------------------------------------------------
// Doc §6.1 / §6.2 - inbound order
// ---------------------------------------------------------------------------

public sealed record InboundOrderDto(
    Guid Id, Guid WarehouseId, string WarehouseCode,
    string OrderNumber,
    Guid? SupplierId, string? SupplierName,
    DateTimeOffset? ExpectedArrivalDate,
    InboundOrderStatus Status,
    string? Notes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<InboundOrderDetailDto> Details,
    IReadOnlyList<ReceiptSummaryDto> Receipts);

public sealed record InboundOrderDetailDto(
    Guid Id, int LineNumber,
    Guid ItemId, string Sku, string ItemName,
    decimal ExpectedQuantity, decimal ReceivedQuantity, decimal OutstandingQuantity,
    Guid UomId, string UomCode,
    string? ExpectedLotNumber, DateTimeOffset? ExpectedExpirationDate,
    bool IsFullyReceived);

public sealed record InboundOrderSummaryDto(
    Guid Id, string OrderNumber, Guid WarehouseId,
    string? SupplierName, DateTimeOffset? ExpectedArrivalDate,
    InboundOrderStatus Status, int LineCount,
    decimal ExpectedQuantity, decimal ReceivedQuantity);

public sealed record CreateInboundOrderRequest(
    Guid WarehouseId,
    string? OrderNumber,
    Guid? SupplierId,
    DateTimeOffset? ExpectedArrivalDate,
    string? Notes,
    IReadOnlyList<CreateInboundOrderLineRequest> Lines);

public sealed record CreateInboundOrderLineRequest(
    Guid ItemId,
    decimal ExpectedQuantity,
    Guid UomId,
    string? ExpectedLotNumber,
    DateTimeOffset? ExpectedExpirationDate);

// ---------------------------------------------------------------------------
// Doc §6.3 - receipt
// ---------------------------------------------------------------------------

/// <summary>POST /api/inbound-orders/{id}/receive (§12).</summary>
public sealed record ReceiveRequest(
    Guid ReceivingLocationId,
    string? Notes,
    IReadOnlyList<ReceiveLineRequest> Lines);

public sealed record ReceiveLineRequest(
    Guid InboundOrderDetailId,
    decimal ReceivedQuantity,
    Guid? ReceivedUomId,
    string? LotNumber,
    DateTimeOffset? ManufactureDate,
    DateTimeOffset? ExpirationDate,
    string? SupplierLotNumber,
    string? SerialNumber,
    Guid? LicensePlateId,
    /// <summary>Receive into a non-default status, e.g. QualityHold pending inspection.</summary>
    Guid? InventoryStatusId);

public sealed record ReceiptDto(
    Guid Id, string ReceiptNumber,
    Guid InboundOrderId, string InboundOrderNumber,
    Guid WarehouseId,
    Guid ReceivingLocationId, string ReceivingLocationCode,
    DateTimeOffset ReceivedAt, string? ReceivedBy, string? Notes,
    Guid CorrelationId,
    IReadOnlyList<ReceiptLineDto> Lines);

public sealed record ReceiptSummaryDto(
    Guid Id, string ReceiptNumber, DateTimeOffset ReceivedAt,
    string? ReceivedBy, int LineCount, decimal TotalQuantity);

public sealed record ReceiptLineDto(
    Guid Id,
    Guid InboundOrderDetailId,
    Guid ItemId, string Sku, string ItemName,
    decimal ReceivedQuantity, Guid ReceivedUomId, string ReceivedUomCode,
    decimal BaseQuantity,
    Guid? LotId, string? LotNumber, DateTimeOffset? ExpirationDate,
    Guid? SerialId, string? SerialNumber,
    Guid? LicensePlateId, string? LicensePlateCode,
    Guid InventoryStatusId, string InventoryStatusCode,
    decimal PutawayQuantity, decimal PendingPutawayQuantity);

// ---------------------------------------------------------------------------
// Doc §6.4 - putaway task
// ---------------------------------------------------------------------------

/// <summary>POST /api/receipts/{id}/create-putaway (§12).</summary>
public sealed record CreatePutawayRequest(
    IReadOnlyList<CreatePutawayLineRequest>? Lines);

public sealed record CreatePutawayLineRequest(
    Guid ReceiptLineId,
    decimal? Quantity,
    /// <summary>
    /// Doc §6.4 - in v1 the destination is chosen by the user. A future slotting
    /// algorithm will populate SuggestedLocationId instead.
    /// </summary>
    Guid? SuggestedLocationId);

/// <summary>POST /api/putaway-tasks/{id}/complete (§12).</summary>
public sealed record CompletePutawayRequest(
    Guid ActualLocationId,
    decimal? Quantity,
    string? Notes);

public sealed record PutawayTaskDto(
    Guid Id, Guid WarehouseId,
    Guid ReceiptLineId,
    Guid ItemId, string Sku, string ItemName,
    Guid FromLocationId, string FromLocationCode,
    Guid? SuggestedLocationId, string? SuggestedLocationCode,
    Guid? ActualLocationId, string? ActualLocationCode,
    decimal Quantity,
    PutawayTaskStatus Status,
    string? RecommendationReason,
    string? AssignedTo,
    Guid? LotId, string? LotNumber,
    Guid? SerialId, string? SerialNumber,
    Guid? LicensePlateId,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

public sealed class InboundOrderQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? SupplierId { get; set; }
    public InboundOrderStatus? Status { get; set; }
}

public sealed class PutawayTaskQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public PutawayTaskStatus? Status { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? ReceiptId { get; set; }
}
