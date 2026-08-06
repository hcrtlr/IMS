using IMS.Application.Common.Models;
using IMS.Domain.Enums;

namespace IMS.Application.Features.Counting;

// ---------------------------------------------------------------------------
// Section 9 of the source document is MISSING. Everything here is reconstructed
// by analogy with the documented inbound/outbound task patterns - see
// docs/ASSUMPTIONS.md section E for the full list of inferences.
// ---------------------------------------------------------------------------

public sealed record CountPlanDto(
    Guid Id, Guid WarehouseId, string PlanNumber, string? Name,
    CountType CountType, CountSelectionMode SelectionMode,
    Guid? ZoneId, string? ZoneCode,
    Guid? ItemId, string? ItemSku,
    CountPlanStatus Status,
    DateTimeOffset? ScheduledDate, DateTimeOffset? ReleasedAt, DateTimeOffset? CompletedAt,
    bool BlockAllocationDuringCount,
    string? Notes,
    // --- audit ---
    string? CreatedBy, DateTimeOffset CreatedAt,
    string? UpdatedBy, DateTimeOffset? UpdatedAt,
    int TaskCount, int CountedTaskCount, int VarianceCount,
    IReadOnlyList<CountTaskDto> Tasks);

public sealed record CountPlanSummaryDto(
    Guid Id, string PlanNumber, string? Name, Guid WarehouseId,
    CountType CountType, CountPlanStatus Status,
    DateTimeOffset? ScheduledDate, string? CreatedBy, DateTimeOffset CreatedAt,
    int TaskCount, int CountedTaskCount, int VarianceCount);

public sealed record CreateCountPlanRequest(
    Guid WarehouseId,
    string? PlanNumber,
    string? Name,
    CountType? CountType,
    CountSelectionMode SelectionMode,
    Guid? ZoneId,
    Guid? ItemId,
    IReadOnlyList<Guid>? LocationIds,
    DateTimeOffset? ScheduledDate,
    bool BlockAllocationDuringCount = false,
    string? Notes = null);

public sealed record CountTaskDto(
    Guid Id, Guid CountPlanId, Guid WarehouseId,
    Guid LocationId, string LocationCode,
    Guid ItemId, string Sku, string ItemName,
    Guid InventoryStatusId, string StatusCode,
    Guid? LotId, string? LotNumber,
    Guid? SerialId, string? SerialNumber,
    Guid? LicensePlateId, string? LicensePlateCode,
    Guid? InventoryBalanceId,
    /// <summary>
    /// The system's on-hand snapshot taken when the task was generated. Hidden from the
    /// counter in the UI so the count is blind and not anchored to the expectation.
    /// </summary>
    decimal SystemQuantity,
    decimal? CountedQuantity,
    decimal? Variance,
    bool HasVariance,
    CountTaskStatus Status,
    // --- audit ---
    string? AssignedTo,
    string? CountedBy, DateTimeOffset? CountedAt,
    string? CreatedBy, DateTimeOffset CreatedAt,
    Guid? InventoryAdjustmentId,
    string? Notes);

/// <summary>POST /api/count-tasks/{id}/complete (§12).</summary>
public sealed record CompleteCountRequest(
    decimal CountedQuantity,
    string? Notes);

public sealed record InventoryAdjustmentDto(
    Guid Id, string AdjustmentNumber,
    Guid WarehouseId,
    Guid LocationId, string LocationCode,
    Guid ItemId, string Sku, string ItemName,
    Guid InventoryStatusId, string StatusCode,
    Guid? LotId, string? LotNumber,
    Guid? SerialId, string? SerialNumber,
    Guid? LicensePlateId,
    Guid? InventoryBalanceId,
    decimal SystemQuantity,
    decimal CountedQuantity,
    decimal AdjustmentQuantity,
    decimal? QuantityBeforeApproval,
    decimal? QuantityAfterApproval,
    bool DriftedBeforeApproval,
    AdjustmentReason Reason,
    AdjustmentStatus Status,
    Guid? CountTaskId,
    Guid? InventoryTransactionId,
    // --- audit ---
    string? RequestedBy, DateTimeOffset CreatedAt,
    string? ApprovedBy, DateTimeOffset? ApprovedAt,
    string? RejectionReason,
    string? Notes,
    Guid CorrelationId);

/// <summary>Raises a standalone adjustment, e.g. a damage write-off outside a count.</summary>
public sealed record CreateAdjustmentRequest(
    Guid WarehouseId,
    Guid LocationId,
    Guid ItemId,
    Guid? InventoryStatusId,
    Guid? LotId,
    Guid? SerialId,
    Guid? LicensePlateId,
    /// <summary>The corrected on-hand quantity being proposed.</summary>
    decimal CountedQuantity,
    AdjustmentReason Reason,
    string? Notes);

public sealed record ApproveAdjustmentRequest(string? Notes);
public sealed record RejectAdjustmentRequest(string RejectionReason);

/// <summary>
/// The complete audit trail for one adjustment: who raised it, who counted (when it came
/// from a count), who approved or rejected it, and the inventory values before and after.
/// </summary>
public sealed record AdjustmentAuditDto(
    Guid AdjustmentId,
    string AdjustmentNumber,
    string Sku,
    string LocationCode,
    string? LotNumber,
    AdjustmentReason Reason,
    AdjustmentStatus Status,
    IReadOnlyList<AuditEventDto> Timeline,
    decimal SystemQuantityAtRaise,
    decimal ProposedQuantity,
    decimal? QuantityBeforeApproval,
    decimal? QuantityAfterApproval,
    decimal AdjustmentQuantity,
    bool DriftedBeforeApproval,
    Guid? InventoryTransactionId,
    Guid CorrelationId);

public sealed record AuditEventDto(
    string Event,
    string? Actor,
    DateTimeOffset? At,
    string? Detail);

public sealed class CountPlanQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public CountPlanStatus? Status { get; set; }
    public CountType? CountType { get; set; }
}

public sealed class CountTaskQuery : PagedQuery
{
    public Guid? CountPlanId { get; set; }
    public Guid? WarehouseId { get; set; }
    public CountTaskStatus? Status { get; set; }
    public string? AssignedTo { get; set; }
}

public sealed class AdjustmentQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public AdjustmentStatus? Status { get; set; }
    public AdjustmentReason? Reason { get; set; }
    public Guid? ItemId { get; set; }
}
