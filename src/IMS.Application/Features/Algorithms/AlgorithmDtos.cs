using IMS.Application.Common.Models;
using IMS.Domain.Enums;

namespace IMS.Application.Features.Algorithms;

// ---------------------------------------------------------------------------
// Faz 6 "Algorithm Readiness". Doc §10 is explicit that algorithms are NOT built
// in v1: "Ilk surumde algoritmalar gelistirilmeyecek olsa bile asagidaki veriler
// saklanmalidir." These endpoints therefore expose the DATA those algorithms will
// need, and let a future engine record its output - nothing here makes a decision.
// ---------------------------------------------------------------------------

public sealed record AlgorithmConfigurationDto(
    Guid Id, Guid? WarehouseId,
    string AlgorithmName, string ParameterKey, string ParameterValue,
    string ValueType, string? Description,
    bool IsActive, int Version, int Priority,
    string? CreatedBy, DateTimeOffset CreatedAt,
    string? UpdatedBy, DateTimeOffset? UpdatedAt);

public sealed record UpsertAlgorithmConfigurationRequest(
    Guid? WarehouseId,
    string AlgorithmName,
    string ParameterKey,
    string ParameterValue,
    string? ValueType,
    string? Description,
    int? Priority);

public sealed record SlottingRecommendationDto(
    Guid Id, Guid WarehouseId,
    Guid ItemId, string Sku,
    Guid? CurrentLocationId, string? CurrentLocationCode,
    Guid RecommendedLocationId, string RecommendedLocationCode,
    decimal? Score, string? RecommendationReason,
    string? AlgorithmName, string? AlgorithmVersion,
    DateTimeOffset GeneratedAt,
    bool? WasAccepted, DateTimeOffset? DecidedAt, string? DecidedBy);

public sealed record CreateSlottingRecommendationRequest(
    Guid WarehouseId, Guid ItemId,
    Guid? CurrentLocationId, Guid RecommendedLocationId,
    decimal? Score, string? RecommendationReason,
    string? AlgorithmName, string? AlgorithmVersion);

public sealed record DecideSlottingRecommendationRequest(bool Accepted);

public sealed record PickingPlanDto(
    Guid Id, Guid WarehouseId, string PlanNumber,
    string? AlgorithmName, string? AlgorithmVersion, string? BatchingStrategy,
    DateTimeOffset GeneratedAt,
    int TotalStops,
    decimal? EstimatedTravelDistance, decimal? ActualTravelDistance,
    int? EstimatedDurationSeconds, int? ActualDurationSeconds,
    string? AssignedTo, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<PickingRouteStopDto> Stops);

public sealed record PickingRouteStopDto(
    Guid Id, int StopSequence,
    Guid LocationId, string LocationCode,
    Guid? PickTaskId, Guid? ItemId, decimal? Quantity,
    decimal? DistanceFromPrevious,
    DateTimeOffset? ArrivedAt, DateTimeOffset? DepartedAt);

public sealed record CreatePickingPlanRequest(
    Guid WarehouseId,
    string? PlanNumber,
    string? AlgorithmName,
    string? AlgorithmVersion,
    string? BatchingStrategy,
    decimal? EstimatedTravelDistance,
    int? EstimatedDurationSeconds,
    string? AssignedTo,
    IReadOnlyList<CreatePickingRouteStopRequest> Stops);

public sealed record CreatePickingRouteStopRequest(
    int StopSequence, Guid LocationId, Guid? PickTaskId,
    Guid? ItemId, decimal? Quantity, decimal? DistanceFromPrevious);

public sealed record OrderHistoryDto(
    Guid Id, Guid OrderId, string OrderNumber, Guid OrderDetailId,
    Guid ItemId, string Sku,
    Guid? CustomerId, OrderType OrderType, int Priority,
    string? Carrier, string? ServiceLevel,
    DateTimeOffset OrderDate, DateTimeOffset? RequiredShipDate, DateTimeOffset ShippedAt,
    decimal OrderedQuantity, decimal ShippedQuantity,
    int OrderLineCount, Guid? PickedFromLocationId,
    int? FulfillmentDurationSeconds);

/// <summary>
/// Demand statistics per item, derived from OrderHistorySnapshot. This is the input a
/// slotting engine needs to decide what belongs near the pick face - the data §10
/// requires be captured, surfaced but not acted upon.
/// </summary>
public sealed record ItemDemandStatsDto(
    Guid ItemId, string Sku, string ItemName,
    int OrderLineCount, decimal TotalShippedQuantity,
    decimal AverageLineQuantity,
    DateTimeOffset FirstShippedAt, DateTimeOffset LastShippedAt,
    int DistinctOrderCount,
    /// <summary>Lines per day over the observed window - the classic slotting velocity input.</summary>
    decimal LinesPerDay);

/// <summary>
/// Reports which §10 data points are actually populated, so the readiness of the
/// groundwork can be inspected rather than assumed.
/// </summary>
public sealed record AlgorithmReadinessDto(
    Guid WarehouseId,
    IReadOnlyList<ReadinessCheckDto> Checks,
    int TotalChecks,
    int PopulatedChecks);

public sealed record ReadinessCheckDto(
    string Category,
    string DataPoint,
    string DocReference,
    int TotalRecords,
    int PopulatedRecords,
    bool IsReady);

public sealed class OrderHistoryQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
}

// ---------------------------------------------------------------------------
// Putaway planning. Unlike the records above, this one does decide: given an item
// and a quantity it works out which locations the stock should go to and how many
// units in each. See PutawayPlanner for how eligibility and ranking are separated.
// ---------------------------------------------------------------------------

public sealed record PutawayPlanDto(
    Guid ItemId,
    string Sku,
    string ItemName,
    decimal RequestedQuantity,
    decimal PlannedQuantity,
    decimal UnplannedQuantity,
    decimal? UnitWeight,
    decimal? UnitVolume,
    decimal LinesPerDay,
    bool IsFastMover,
    IReadOnlyList<PutawayPlanLineDto> Lines,
    IReadOnlyList<string> Notes);

public sealed record PutawayPlanLineDto(
    Guid LocationId,
    string LocationCode,
    string ZoneCode,
    ZoneType ZoneType,
    LocationType LocationType,
    decimal Quantity,
    /// <summary>Capacity ceiling in units; null when the location declares no limit.</summary>
    decimal? UnitsThatFit,
    decimal? RemainingWeight,
    decimal? RemainingVolume,
    decimal? DistanceToPacking,
    int? AccessibilityScore,
    bool AlreadyHoldsItem,
    double Score,
    string Reason);
