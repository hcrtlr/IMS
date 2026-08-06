using IMS.Api.Security;
using IMS.Application.Features.MasterData;
using IMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>Doc §3.3 - zones. API surface per §12 (POST /api/zones).</summary>
[ApiController]
[Route("api/zones")]
[Authorize]
[Produces("application/json")]
public class ZonesController : ControllerBase
{
    private readonly ZoneService _service;

    public ZonesController(ZoneService service) => _service = service;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<ZoneDto>>> List(
        [FromQuery] Guid? warehouseId, [FromQuery] ZoneType? zoneType, CancellationToken ct)
        => Ok(await _service.ListAsync(warehouseId, zoneType, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<ZoneDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ZoneDto>> Create(CreateZoneRequest request, CancellationToken ct)
    {
        var zone = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = zone.Id }, zone);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<ZoneDto>> Update(Guid id, UpdateZoneRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _service.DeactivateAsync(id, ct);
        return NoContent();
    }
}
