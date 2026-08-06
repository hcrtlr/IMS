using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.Algorithms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Faz 6 - algorithm readiness. Doc §10 defers the algorithms themselves; these endpoints
/// expose the data they will need and let a future engine record its output.
/// Nothing here makes a slotting or routing decision.
/// </summary>
[ApiController]
[Route("api/algorithms")]
[Authorize]
[Produces("application/json")]
public class AlgorithmsController : ControllerBase
{
    private readonly AlgorithmReadinessService _service;

    public AlgorithmsController(AlgorithmReadinessService service) => _service = service;

    /// <summary>
    /// Reports how much of the §10 data groundwork is populated, per data point, so
    /// readiness can be inspected rather than assumed.
    /// </summary>
    [HttpGet("readiness")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<AlgorithmReadinessDto>> Readiness(
        [FromQuery] Guid warehouseId, CancellationToken ct)
        => Ok(await _service.GetReadinessAsync(warehouseId, ct));

    // --- Configuration ------------------------------------------------------

    [HttpGet("configurations")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<AlgorithmConfigurationDto>>> ListConfigurations(
        [FromQuery] string? algorithmName, [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Ok(await _service.ListConfigurationsAsync(algorithmName, warehouseId, ct));

    /// <summary>Creates or updates one tunable parameter; updating bumps its version.</summary>
    [HttpPut("configurations")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<AlgorithmConfigurationDto>> UpsertConfiguration(
        UpsertAlgorithmConfigurationRequest request, CancellationToken ct)
        => Ok(await _service.UpsertConfigurationAsync(request, ct));

    // --- Slotting recommendations -------------------------------------------

    [HttpGet("slotting-recommendations")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<SlottingRecommendationDto>>> ListRecommendations(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? itemId, CancellationToken ct)
        => Ok(await _service.ListRecommendationsAsync(warehouseId, itemId, ct));

    /// <summary>Records a recommendation produced by a future slotting engine.</summary>
    [HttpPost("slotting-recommendations")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<SlottingRecommendationDto>> RecordRecommendation(
        CreateSlottingRecommendationRequest request, CancellationToken ct)
        => Ok(await _service.RecordRecommendationAsync(request, ct));

    /// <summary>
    /// Records whether an operator accepted the suggestion. The acceptance rate is how
    /// a future engine's quality gets measured against reality.
    /// </summary>
    [HttpPost("slotting-recommendations/{id:guid}/decide")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<SlottingRecommendationDto>> DecideRecommendation(
        Guid id, DecideSlottingRecommendationRequest request, CancellationToken ct)
        => Ok(await _service.DecideRecommendationAsync(id, request, ct));

    // --- Picking plans ------------------------------------------------------

    [HttpPost("picking-plans")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<PickingPlanDto>> RecordPickingPlan(
        CreatePickingPlanRequest request, CancellationToken ct)
        => Ok(await _service.RecordPickingPlanAsync(request, ct));

    [HttpGet("picking-plans/{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PickingPlanDto>> GetPickingPlan(Guid id, CancellationToken ct)
        => Ok(await _service.GetPickingPlanAsync(id, ct));

    // --- Historical demand --------------------------------------------------

    /// <summary>Shipped-line history, written automatically on every shipment.</summary>
    [HttpGet("order-history")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<OrderHistoryDto>>> OrderHistory(
        [FromQuery] OrderHistoryQuery query, CancellationToken ct)
        => Ok(await _service.ListOrderHistoryAsync(query, ct));

    /// <summary>Per-item demand velocity - the primary input to a future slotting engine.</summary>
    [HttpGet("demand-stats")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<ItemDemandStatsDto>>> DemandStats(
        [FromQuery] Guid warehouseId, CancellationToken ct)
        => Ok(await _service.GetDemandStatsAsync(warehouseId, ct));
}
