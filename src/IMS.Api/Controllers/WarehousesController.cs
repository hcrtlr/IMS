using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.MasterData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>Doc §3.1 / §3.2 - accounts and warehouses. API surface per §12.</summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class WarehousesController : ControllerBase
{
    private readonly WarehouseService _service;

    public WarehousesController(WarehouseService service) => _service = service;

    // --- Accounts ------------------------------------------------------------

    /// <summary>Returns the caller's account.</summary>
    [HttpGet("accounts")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> ListAccounts(CancellationToken ct)
        => Ok(await _service.ListAccountsAsync(ct));

    [HttpGet("accounts/{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<AccountDto>> GetAccount(Guid id, CancellationToken ct)
        => Ok(await _service.GetAccountAsync(id, ct));

    [HttpPut("accounts/{id:guid}")]
    [Authorize(Policy = Policies.Administration)]
    public async Task<ActionResult<AccountDto>> UpdateAccount(
        Guid id, UpdateAccountRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAccountAsync(id, request, ct));

    // --- Warehouses (§12: POST /api/warehouses) ------------------------------

    [HttpGet("warehouses")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<WarehouseDto>>> ListWarehouses(
        [FromQuery] PagedQuery query, CancellationToken ct)
        => Ok(await _service.ListWarehousesAsync(query, ct));

    [HttpGet("warehouses/{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<WarehouseDto>> GetWarehouse(Guid id, CancellationToken ct)
        => Ok(await _service.GetWarehouseAsync(id, ct));

    [HttpPost("warehouses")]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<WarehouseDto>> CreateWarehouse(
        CreateWarehouseRequest request, CancellationToken ct)
    {
        var warehouse = await _service.CreateWarehouseAsync(request, ct);
        return CreatedAtAction(nameof(GetWarehouse), new { id = warehouse.Id }, warehouse);
    }

    [HttpPut("warehouses/{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<WarehouseDto>> UpdateWarehouse(
        Guid id, UpdateWarehouseRequest request, CancellationToken ct)
        => Ok(await _service.UpdateWarehouseAsync(id, request, ct));

    /// <summary>Deactivates a warehouse. Refused while it still holds stock.</summary>
    [HttpDelete("warehouses/{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeactivateWarehouse(Guid id, CancellationToken ct)
    {
        await _service.DeactivateWarehouseAsync(id, ct);
        return NoContent();
    }
}
