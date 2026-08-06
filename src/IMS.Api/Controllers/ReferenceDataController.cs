using IMS.Api.Security;
using IMS.Application.Features.MasterData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Supporting master data: categories, units of measure, dynamic attribute definitions
/// (§4.2), suppliers and customers.
/// </summary>
[ApiController]
[Route("api")]
[Authorize]
[Produces("application/json")]
public class ReferenceDataController : ControllerBase
{
    private readonly ReferenceDataService _service;

    public ReferenceDataController(ReferenceDataService service) => _service = service;

    // --- Item categories -----------------------------------------------------

    [HttpGet("item-categories")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<ItemCategoryDto>>> ListCategories(CancellationToken ct)
        => Ok(await _service.ListCategoriesAsync(ct));

    [HttpPost("item-categories")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<ItemCategoryDto>> CreateCategory(
        CreateItemCategoryRequest request, CancellationToken ct)
        => Ok(await _service.CreateCategoryAsync(request, ct));

    [HttpPut("item-categories/{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<ItemCategoryDto>> UpdateCategory(
        Guid id, UpdateItemCategoryRequest request, CancellationToken ct)
        => Ok(await _service.UpdateCategoryAsync(id, request, ct));

    // --- Units of measure (doc §4.3) -----------------------------------------

    [HttpGet("units-of-measure")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<UnitOfMeasureDto>>> ListUoms(CancellationToken ct)
        => Ok(await _service.ListUomsAsync(ct));

    [HttpPost("units-of-measure")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<UnitOfMeasureDto>> CreateUom(
        CreateUnitOfMeasureRequest request, CancellationToken ct)
        => Ok(await _service.CreateUomAsync(request, ct));

    // --- Attribute definitions (doc §4.2) ------------------------------------

    [HttpGet("attribute-definitions")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<AttributeDefinitionDto>>> ListAttributeDefinitions(
        CancellationToken ct)
        => Ok(await _service.ListAttributeDefinitionsAsync(ct));

    [HttpPost("attribute-definitions")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<AttributeDefinitionDto>> CreateAttributeDefinition(
        CreateAttributeDefinitionRequest request, CancellationToken ct)
        => Ok(await _service.CreateAttributeDefinitionAsync(request, ct));

    [HttpPut("attribute-definitions/{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<AttributeDefinitionDto>> UpdateAttributeDefinition(
        Guid id, UpdateAttributeDefinitionRequest request, CancellationToken ct)
        => Ok(await _service.UpdateAttributeDefinitionAsync(id, request, ct));

    // --- Suppliers -----------------------------------------------------------

    [HttpGet("suppliers")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> ListSuppliers(CancellationToken ct)
        => Ok(await _service.ListSuppliersAsync(ct));

    [HttpPost("suppliers")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<SupplierDto>> CreateSupplier(
        CreateSupplierRequest request, CancellationToken ct)
        => Ok(await _service.CreateSupplierAsync(request, ct));

    [HttpPut("suppliers/{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<SupplierDto>> UpdateSupplier(
        Guid id, UpdateSupplierRequest request, CancellationToken ct)
        => Ok(await _service.UpdateSupplierAsync(id, request, ct));

    // --- Customers -----------------------------------------------------------

    [HttpGet("customers")]
    [Authorize(Policy = Policies.ReadOnly)]
    public async Task<ActionResult<IReadOnlyList<CustomerDto>>> ListCustomers(CancellationToken ct)
        => Ok(await _service.ListCustomersAsync(ct));

    [HttpPost("customers")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<CustomerDto>> CreateCustomer(
        CreateCustomerRequest request, CancellationToken ct)
        => Ok(await _service.CreateCustomerAsync(request, ct));

    [HttpPut("customers/{id:guid}")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult<CustomerDto>> UpdateCustomer(
        Guid id, UpdateCustomerRequest request, CancellationToken ct)
        => Ok(await _service.UpdateCustomerAsync(id, request, ct));
}
