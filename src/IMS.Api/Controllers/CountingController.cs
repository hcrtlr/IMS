using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.Counting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Faz 5 - cycle counting. API surface per §12: POST /api/count-plans and
/// POST /api/count-tasks/{id}/complete.
///
/// Section 9 of the source document is missing; this workflow is reconstructed by
/// analogy. See docs/ASSUMPTIONS.md section E.
/// </summary>
[ApiController]
[Route("api/count-plans")]
[Authorize]
[Produces("application/json")]
public class CountPlansController : ControllerBase
{
    private readonly CountingService _counting;

    public CountPlansController(CountingService counting) => _counting = counting;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<CountPlanSummaryDto>>> List(
        [FromQuery] CountPlanQuery query, CancellationToken ct)
        => Ok(await _counting.ListPlansAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<CountPlanDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _counting.GetPlanAsync(id, ct));

    /// <summary>POST /api/count-plans (§12) - creates the plan in Draft.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.WarehouseOperations)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<CountPlanDto>> Create(
        CreateCountPlanRequest request, CancellationToken ct)
    {
        var plan = await _counting.CreatePlanAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, plan);
    }

    /// <summary>
    /// Releases the plan and generates one count task per stock combination in scope.
    /// A ByLocation plan must supply its location list here.
    /// </summary>
    [HttpPost("{id:guid}/release")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<CountPlanDto>> Release(
        Guid id, [FromBody] IReadOnlyList<Guid>? locationIds, CancellationToken ct)
        => Ok(await _counting.ReleasePlanAsync(id, locationIds, ct));
}

/// <summary>Count tasks - the blind count itself.</summary>
[ApiController]
[Route("api/count-tasks")]
[Authorize]
[Produces("application/json")]
public class CountTasksController : ControllerBase
{
    private readonly CountingService _counting;

    public CountTasksController(CountingService counting) => _counting = counting;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<CountTaskDto>>> List(
        [FromQuery] CountTaskQuery query, CancellationToken ct)
        => Ok(await _counting.ListTasksAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<CountTaskDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _counting.GetTaskAsync(id, ct));

    /// <summary>
    /// POST /api/count-tasks/{id}/complete (§12). Records what the counter found and by
    /// whom. Stock is NOT changed here: a variance raises a pending adjustment instead,
    /// so there is exactly one auditable moment where stock moves.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<CountTaskDto>> Complete(
        Guid id, CompleteCountRequest request, CancellationToken ct)
        => Ok(await _counting.CompleteCountAsync(id, request, ct));
}

/// <summary>
/// Faz 5 - inventory adjustments, including the §12 approval endpoint.
/// </summary>
[ApiController]
[Route("api/inventory-adjustments")]
[Authorize]
[Produces("application/json")]
public class InventoryAdjustmentsController : ControllerBase
{
    private readonly CountingService _counting;

    public InventoryAdjustmentsController(CountingService counting) => _counting = counting;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<InventoryAdjustmentDto>>> List(
        [FromQuery] AdjustmentQuery query, CancellationToken ct)
        => Ok(await _counting.ListAdjustmentsAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<InventoryAdjustmentDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _counting.GetAdjustmentAsync(id, ct));

    /// <summary>
    /// The full audit trail: who created the plan, who counted, who approved or rejected,
    /// and the on-hand values immediately before and after the change.
    /// </summary>
    [HttpGet("{id:guid}/audit")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<AdjustmentAuditDto>> Audit(Guid id, CancellationToken ct)
        => Ok(await _counting.GetAuditTrailAsync(id, ct));

    /// <summary>Raises a standalone adjustment outside a count, e.g. a damage write-off.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.WarehouseOperations)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<InventoryAdjustmentDto>> Create(
        CreateAdjustmentRequest request, CancellationToken ct)
    {
        var adjustment = await _counting.CreateAdjustmentAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = adjustment.Id }, adjustment);
    }

    /// <summary>
    /// POST /api/inventory-adjustments/{id}/approve (§12).
    ///
    /// The only place a count or adjustment changes stock. Requires a manager or admin,
    /// and the approver must differ from the person who raised it.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = Policies.ApproveAdjustments)]
    public async Task<ActionResult<InventoryAdjustmentDto>> Approve(
        Guid id, [FromBody] ApproveAdjustmentRequest? request, CancellationToken ct)
        => Ok(await _counting.ApproveAdjustmentAsync(id, request, ct));

    /// <summary>Rejects an adjustment. No stock moves; the decision and reason are recorded.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = Policies.ApproveAdjustments)]
    public async Task<ActionResult<InventoryAdjustmentDto>> Reject(
        Guid id, RejectAdjustmentRequest request, CancellationToken ct)
        => Ok(await _counting.RejectAdjustmentAsync(id, request, ct));
}
