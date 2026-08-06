using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.MasterData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Doc §3.4 - locations. API surface per §12:
/// POST /api/locations and GET /api/locations/available.
/// </summary>
[ApiController]
[Route("api/locations")]
[Authorize]
[Produces("application/json")]
public class LocationsController : ControllerBase
{
    private readonly LocationService _service;

    public LocationsController(LocationService service) => _service = service;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<LocationDto>>> List(
        [FromQuery] Guid? warehouseId,
        [FromQuery] Guid? zoneId,
        [FromQuery] PagedQuery query,
        CancellationToken ct)
        => Ok(await _service.ListAsync(warehouseId, zoneId, query, ct));

    /// <summary>
    /// Doc §12 - candidate locations for putaway. When itemId is supplied the result
    /// excludes locations that would violate the temperature, hazmat, category or
    /// item-mixing constraints (rules §11.7 and §11.8).
    /// </summary>
    [HttpGet("available")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> Available(
        [FromQuery] AvailableLocationQuery query, CancellationToken ct)
        => Ok(await _service.GetAvailableAsync(query, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<LocationDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<LocationDto>> Create(CreateLocationRequest request, CancellationToken ct)
    {
        var location = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = location.Id }, location);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<LocationDto>> Update(
        Guid id, UpdateLocationRequest request, CancellationToken ct)
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

/// <summary>Doc §3.5 - location profiles.</summary>
[ApiController]
[Route("api/location-profiles")]
[Authorize]
[Produces("application/json")]
public class LocationProfilesController : ControllerBase
{
    private readonly LocationProfileService _service;

    public LocationProfilesController(LocationProfileService service) => _service = service;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<LocationProfileDto>>> List(CancellationToken ct)
        => Ok(await _service.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<LocationProfileDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, ct));

    [HttpPost]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<LocationProfileDto>> Create(
        CreateLocationProfileRequest request, CancellationToken ct)
    {
        var profile = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = profile.Id }, profile);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<LocationProfileDto>> Update(
        Guid id, UpdateLocationProfileRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));
}
