using IMS.Api.Security;
using IMS.Application.Common.Models;
using IMS.Application.Features.MasterData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Doc §4 - item master, dynamic attributes, UOM conversions and barcodes.
/// API surface per §12 (POST/GET/PUT /api/items).
/// </summary>
[ApiController]
[Route("api/items")]
[Authorize]
[Produces("application/json")]
public class ItemsController : ControllerBase
{
    private readonly ItemService _service;

    public ItemsController(ItemService service) => _service = service;

    [HttpGet]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<PagedResult<ItemSummaryDto>>> List(
        [FromQuery] PagedQuery query,
        [FromQuery] bool? isActive,
        [FromQuery] Guid? categoryId,
        CancellationToken ct)
        => Ok(await _service.ListAsync(query, isActive, categoryId, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<ItemDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _service.GetAsync(id, ct));

    /// <summary>Looks an item up by SKU, which is unique within the account (§4.1).</summary>
    [HttpGet("by-sku/{sku}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<ItemDto>> GetBySku(string sku, CancellationToken ct)
        => Ok(await _service.GetBySkuAsync(sku, ct));

    /// <summary>Resolves a scanned barcode to its item (§4.4).</summary>
    [HttpGet("by-barcode/{barcode}")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<ItemDto>> GetByBarcode(string barcode, CancellationToken ct)
        => Ok(await _service.FindByBarcodeAsync(barcode, ct));

    [HttpPost]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<ItemDto>> Create(CreateItemRequest request, CancellationToken ct)
    {
        var item = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, item);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<ItemDto>> Update(Guid id, UpdateItemRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAsync(id, request, ct));

    // --- Dynamic attributes (doc §4.2) ---------------------------------------

    /// <summary>Sets or replaces one attribute value on an item.</summary>
    [HttpPut("{id:guid}/attributes")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<ItemDto>> SetAttribute(
        Guid id, SetItemAttributeRequest request, CancellationToken ct)
        => Ok(await _service.SetAttributeAsync(id, request, ct));

    [HttpDelete("{id:guid}/attributes/{attributeDefinitionId:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveAttribute(
        Guid id, Guid attributeDefinitionId, CancellationToken ct)
    {
        await _service.RemoveAttributeAsync(id, attributeDefinitionId, ct);
        return NoContent();
    }

    // --- UOM conversions (doc §4.3) ------------------------------------------

    [HttpPost("{id:guid}/uoms")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<ItemDto>> AddUom(
        Guid id, CreateItemUomRequest request, CancellationToken ct)
        => Ok(await _service.AddUomAsync(id, request, ct));

    [HttpDelete("{id:guid}/uoms/{itemUomId:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveUom(Guid id, Guid itemUomId, CancellationToken ct)
    {
        await _service.RemoveUomAsync(id, itemUomId, ct);
        return NoContent();
    }

    // --- Barcodes (doc §4.4) -------------------------------------------------

    [HttpPost("{id:guid}/barcodes")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<ItemDto>> AddBarcode(
        Guid id, CreateItemBarcodeRequest request, CancellationToken ct)
        => Ok(await _service.AddBarcodeAsync(id, request, ct));

    [HttpDelete("{id:guid}/barcodes/{barcodeId:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveBarcode(Guid id, Guid barcodeId, CancellationToken ct)
    {
        await _service.RemoveBarcodeAsync(id, barcodeId, ct);
        return NoContent();
    }
}
