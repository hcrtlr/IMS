using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.Outbound;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Doc §7 - outbound. API surface per §12: POST /api/orders and the release, allocate,
/// create-pick-tasks and ship actions.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orders;
    private readonly AllocationService _allocation;
    private readonly PickingService _picking;
    private readonly ShippingService _shipping;

    public OrdersController(
        OrderService orders,
        AllocationService allocation,
        PickingService picking,
        ShippingService shipping)
    {
        _orders = orders;
        _allocation = allocation;
        _picking = picking;
        _shipping = shipping;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> List(
        [FromQuery] OrderQuery query, CancellationToken ct)
        => Ok(await _orders.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _orders.GetAsync(id, ct));

    /// <summary>POST /api/orders (§12) - creates the order in Draft.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.WarehouseOperations)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDto>> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await _orders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Draft -> Created.</summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<OrderDto>> Confirm(Guid id, CancellationToken ct)
        => Ok(await _orders.ConfirmAsync(id, ct));

    /// <summary>POST /api/orders/{id}/release (§12). Created -> Released.</summary>
    [HttpPost("{id:guid}/release")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<OrderDto>> Release(Guid id, CancellationToken ct)
        => Ok(await _orders.ReleaseAsync(id, ct));

    /// <summary>
    /// POST /api/orders/{id}/allocate (§12). Reserves stock without touching on-hand
    /// (rule §11.2). A line that cannot be fully covered is partially allocated and its
    /// shortfall reported, so the order never falsely reaches Allocated.
    /// </summary>
    [HttpPost("{id:guid}/allocate")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<AllocationResultDto>> Allocate(
        Guid id, [FromBody] AllocateRequest? request, CancellationToken ct)
        => Ok(await _allocation.AllocateAsync(id, request, ct));

    /// <summary>Releases reservations back to available stock.</summary>
    [HttpPost("{id:guid}/deallocate")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<AllocationResultDto>> Deallocate(
        Guid id, [FromBody] IReadOnlyList<Guid>? allocationIds, CancellationToken ct)
        => Ok(await _allocation.DeallocateAsync(id, allocationIds, ct));

    /// <summary>POST /api/orders/{id}/create-pick-tasks (§12).</summary>
    [HttpPost("{id:guid}/create-pick-tasks")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<IReadOnlyList<PickTaskDto>>> CreatePickTasks(
        Guid id, [FromBody] CreatePickTasksRequest? request, CancellationToken ct)
        => Ok(await _picking.CreatePickTasksAsync(id, request, ct));

    /// <summary>Picked -> Packed.</summary>
    [HttpPost("{id:guid}/pack")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<OrderDto>> Pack(Guid id, CancellationToken ct)
        => Ok(await _orders.PackAsync(id, ct));

    /// <summary>
    /// POST /api/orders/{id}/ship (§12). Rule §11.3 - reduces on-hand and allocated
    /// together, and writes the Faz 6 order-history rows.
    /// </summary>
    [HttpPost("{id:guid}/ship")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<ShipmentDto>> Ship(
        Guid id, [FromBody] ShipRequest? request, CancellationToken ct)
        => Ok(await _shipping.ShipAsync(id, request, ct));

    [HttpGet("{id:guid}/shipments")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<ShipmentDto>>> Shipments(Guid id, CancellationToken ct)
        => Ok(await _shipping.ListForOrderAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _orders.CancelAsync(id, ct));
}

/// <summary>Doc §7.4 - pick tasks.</summary>
[ApiController]
[Route("api/pick-tasks")]
[Authorize]
[Produces("application/json")]
public class PickTasksController : ControllerBase
{
    private readonly PickingService _picking;

    public PickTasksController(PickingService picking) => _picking = picking;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<PickTaskDto>>> List(
        [FromQuery] PickTaskQuery query, CancellationToken ct)
        => Ok(await _picking.ListAsync(query, ct));

    /// <summary>
    /// POST /api/pick-tasks/{id}/complete (§12). Picking less than requested closes the
    /// task as ShortPicked and releases the remainder back to available stock.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<PickTaskDto>> Complete(
        Guid id, CompletePickRequest request, CancellationToken ct)
        => Ok(await _picking.CompletePickAsync(id, request, ct));
}
