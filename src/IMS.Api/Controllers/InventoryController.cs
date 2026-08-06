using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Doc §5 and §8 - inventory balances, movements, status changes and the transaction
/// ledger. API surface per §12 (Inventory group).
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
[Produces("application/json")]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _service;

    public InventoryController(InventoryService service) => _service = service;

    /// <summary>GET /api/inventory - current balances (doc §5.1).</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<InventoryBalanceDto>>> List(
        [FromQuery] InventoryQuery query, CancellationToken ct)
        => Ok(await _service.ListAsync(query, ct));

    /// <summary>GET /api/inventory/by-item/{itemId} - ordered FEFO then FIFO.</summary>
    [HttpGet("by-item/{itemId:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<InventoryBalanceDto>>> ByItem(
        Guid itemId, [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Ok(await _service.ByItemAsync(itemId, warehouseId, ct));

    /// <summary>GET /api/inventory/by-location/{locationId}.</summary>
    [HttpGet("by-location/{locationId:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<InventoryBalanceDto>>> ByLocation(
        Guid locationId, CancellationToken ct)
        => Ok(await _service.ByLocationAsync(locationId, ct));

    /// <summary>Aggregated stock position per item for a warehouse.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<ItemStockSummaryDto>>> Summary(
        [FromQuery] Guid warehouseId, CancellationToken ct)
        => Ok(await _service.SummaryAsync(warehouseId, ct));

    /// <summary>
    /// GET /api/inventory/transactions - the immutable ledger (doc §5.6).
    /// Also serves the Faz 5 transaction report.
    /// </summary>
    [HttpGet("transactions")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<InventoryTransactionDto>>> Transactions(
        [FromQuery] TransactionQuery query, CancellationToken ct)
        => Ok(await _service.TransactionsAsync(query, ct));

    /// <summary>The seven documented inventory statuses (doc §5.2).</summary>
    [HttpGet("statuses")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<InventoryStatusDto>>> Statuses(CancellationToken ct)
        => Ok(await _service.ListStatusesAsync(ct));

    /// <summary>
    /// Faz 2 "Manuel stok girisi" - books stock directly into a location without an
    /// inbound order, creating lot/serial records as the item master requires.
    /// </summary>
    [HttpPost("manual-entry")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InventoryBalanceDto>> ManualEntry(
        ManualStockEntryRequest request, CancellationToken ct)
        => Ok(await _service.ManualEntryAsync(request, ct));

    /// <summary>
    /// POST /api/inventory/movements (§12) - moves stock between locations in a single
    /// database transaction (doc §8).
    /// </summary>
    [HttpPost("movements")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InventoryBalanceDto>> Move(
        InventoryMovementRequest request, CancellationToken ct)
        => Ok(await _service.MoveAsync(request, ct));

    /// <summary>POST /api/inventory/status-change (§12).</summary>
    [HttpPost("status-change")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InventoryBalanceDto>> ChangeStatus(
        StatusChangeRequest request, CancellationToken ct)
        => Ok(await _service.ChangeStatusAsync(request, ct));

    /// <summary>Faz 5 - blocks quantity from allocation without changing its status.</summary>
    [HttpPost("hold")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InventoryBalanceDto>> PlaceHold(
        HoldRequest request, CancellationToken ct)
        => Ok(await _service.PlaceHoldAsync(request, ct));

    /// <summary>Releases previously held quantity back to available.</summary>
    [HttpPost("release-hold")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<InventoryBalanceDto>> ReleaseHold(
        HoldRequest request, CancellationToken ct)
        => Ok(await _service.ReleaseHoldAsync(request, ct));
}

/// <summary>Doc §5.3, §5.4, §5.5 - lots, serial numbers and license plates.</summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class TrackingController : ControllerBase
{
    private readonly TrackingService _service;

    public TrackingController(TrackingService service) => _service = service;

    // --- Lots (§5.3) ---------------------------------------------------------

    [HttpGet("lots")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<LotDto>>> ListLots(
        [FromQuery] Guid? itemId, [FromQuery] bool expiringOnly, CancellationToken ct)
        => Ok(await _service.ListLotsAsync(itemId, expiringOnly, ct));

    [HttpPost("lots")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<LotDto>> CreateLot(CreateLotRequest request, CancellationToken ct)
        => Ok(await _service.CreateLotAsync(request, ct));

    /// <summary>Expired lots still holding stock - the input to a Faz 5 write-off.</summary>
    [HttpGet("lots/expired")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<LotDto>>> ExpiredLots(
        [FromQuery] Guid? warehouseId, CancellationToken ct)
        => Ok(await _service.ExpiredLotsAsync(warehouseId, ct));

    // --- Serial numbers (§5.4) -----------------------------------------------

    [HttpGet("serial-numbers")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<SerialNumberDto>>> ListSerials(
        [FromQuery] Guid? itemId,
        [FromQuery] IMS.Domain.Enums.SerialStatus? status,
        CancellationToken ct)
        => Ok(await _service.ListSerialsAsync(itemId, status, ct));

    [HttpPost("serial-numbers")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<SerialNumberDto>> CreateSerial(
        CreateSerialRequest request, CancellationToken ct)
        => Ok(await _service.CreateSerialAsync(request, ct));

    // --- License plates (§5.5) -----------------------------------------------

    [HttpGet("license-plates")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<LicensePlateDto>>> ListLicensePlates(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? locationId, CancellationToken ct)
        => Ok(await _service.ListLicensePlatesAsync(warehouseId, locationId, ct));

    [HttpPost("license-plates")]
    [Authorize(Policy = Policies.WarehouseOperations)]
    public async Task<ActionResult<LicensePlateDto>> CreateLicensePlate(
        CreateLicensePlateRequest request, CancellationToken ct)
        => Ok(await _service.CreateLicensePlateAsync(request, ct));

    /// <summary>Doc §5.5 - nested container view (Pallet -> Case 1 / Case 2 / ...).</summary>
    [HttpGet("license-plates/{id:guid}/tree")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<LicensePlateTreeDto>> LicensePlateTree(Guid id, CancellationToken ct)
        => Ok(await _service.GetLicensePlateTreeAsync(id, ct));
}
