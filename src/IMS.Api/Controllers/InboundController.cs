using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.Inbound;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Doc §6 - inbound. API surface per §12:
/// POST /api/inbound-orders, POST /api/inbound-orders/{id}/receive.
/// </summary>
[ApiController]
[Route("api/inbound-orders")]
[Authorize]
[Produces("application/json")]
public class InboundOrdersController : ControllerBase
{
    private readonly InboundOrderService _orders;
    private readonly ReceivingService _receiving;

    public InboundOrdersController(InboundOrderService orders, ReceivingService receiving)
    {
        _orders = orders;
        _receiving = receiving;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<InboundOrderSummaryDto>>> List(
        [FromQuery] InboundOrderQuery query, CancellationToken ct)
        => Ok(await _orders.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<InboundOrderDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _orders.GetAsync(id, ct));

    /// <summary>POST /api/inbound-orders (§12) - creates the order in Draft.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.WarehouseOperations)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<InboundOrderDto>> Create(
        CreateInboundOrderRequest request, CancellationToken ct)
    {
        var order = await _orders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Draft -> Expected. Receiving is blocked until an order is confirmed.</summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InboundOrderDto>> Confirm(Guid id, CancellationToken ct)
        => Ok(await _orders.ConfirmAsync(id, ct));

    /// <summary>
    /// POST /api/inbound-orders/{id}/receive (§12) - the doc §6.3 physical intake:
    /// verify, capture quantity and lot/serial, land stock in the receiving location,
    /// and write the inventory transactions, all in one database transaction.
    /// </summary>
    [HttpPost("{id:guid}/receive")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<ReceiptDto>> Receive(
        Guid id, ReceiveRequest request, CancellationToken ct)
        => Ok(await _receiving.ReceiveAsync(id, request, ct));

    /// <summary>Received -> Completed.</summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InboundOrderDto>> Complete(Guid id, CancellationToken ct)
        => Ok(await _orders.CompleteAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InboundOrderDto>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _orders.CancelAsync(id, ct));
}

/// <summary>Doc §6.3 - receipts, and the entry point for creating putaway tasks.</summary>
[ApiController]
[Route("api/receipts")]
[Authorize]
[Produces("application/json")]
public class ReceiptsController : ControllerBase
{
    private readonly ReceivingService _receiving;

    public ReceiptsController(ReceivingService receiving) => _receiving = receiving;

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<ReceiptDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _receiving.GetReceiptAsync(id, ct));

    /// <summary>
    /// POST /api/receipts/{id}/create-putaway (§12). With no body, raises a task for every
    /// receipt line still sitting in the receiving location.
    /// </summary>
    [HttpPost("{id:guid}/create-putaway")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<IReadOnlyList<PutawayTaskDto>>> CreatePutaway(
        Guid id, [FromBody] CreatePutawayRequest? request, CancellationToken ct)
        => Ok(await _receiving.CreatePutawayTasksAsync(id, request, ct));
}

/// <summary>Doc §6.4 - putaway tasks.</summary>
[ApiController]
[Route("api/putaway-tasks")]
[Authorize]
[Produces("application/json")]
public class PutawayTasksController : ControllerBase
{
    private readonly ReceivingService _receiving;

    public PutawayTasksController(ReceivingService receiving) => _receiving = receiving;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<PutawayTaskDto>>> List(
        [FromQuery] PutawayTaskQuery query, CancellationToken ct)
        => Ok(await _receiving.ListPutawayTasksAsync(query, ct));

    /// <summary>
    /// POST /api/putaway-tasks/{id}/complete (§12) - moves the stock from receiving to
    /// the destination the operator chose, checking rule §11.8 compatibility first.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<PutawayTaskDto>> Complete(
        Guid id, CompletePutawayRequest request, CancellationToken ct)
        => Ok(await _receiving.CompletePutawayAsync(id, request, ct));
}
