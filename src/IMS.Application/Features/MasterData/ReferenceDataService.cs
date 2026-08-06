using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.MasterData;

/// <summary>
/// Supporting master data: item categories, units of measure, dynamic attribute
/// definitions (§4.2), suppliers and customers. Categories, suppliers and customers are
/// inferred entities - see docs/ASSUMPTIONS.md.
/// </summary>
public class ReferenceDataService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;

    public ReferenceDataService(IApplicationDbContext db, ScopeGuard scope)
    {
        _db = db;
        _scope = scope;
    }

    // --- Item categories -----------------------------------------------------

    public async Task<IReadOnlyList<ItemCategoryDto>> ListCategoriesAsync(CancellationToken ct = default)
        => await _db.ItemCategories
            .Where(c => c.AccountId == _scope.AccountId)
            .OrderBy(c => c.Code)
            .Select(c => new ItemCategoryDto(
                c.Id, c.Code, c.Name, c.ParentCategoryId,
                c.ParentCategory != null ? c.ParentCategory.Name : null, c.IsActive))
            .ToListAsync(ct);

    public async Task<ItemCategoryDto> CreateCategoryAsync(
        CreateItemCategoryRequest request, CancellationToken ct = default)
    {
        var accountId = _scope.AccountId;

        if (await _db.ItemCategories.AnyAsync(c => c.AccountId == accountId && c.Code == request.Code, ct))
            throw new DuplicateEntityException($"Category code '{request.Code}' already exists.");

        if (request.ParentCategoryId.HasValue)
            await EnsureCategoryAsync(request.ParentCategoryId.Value, ct);

        var category = new ItemCategory
        {
            AccountId = accountId,
            Code = request.Code,
            Name = request.Name,
            ParentCategoryId = request.ParentCategoryId,
            IsActive = true
        };

        _db.ItemCategories.Add(category);
        await _db.SaveChangesAsync(ct);

        return (await ListCategoriesAsync(ct)).First(c => c.Id == category.Id);
    }

    public async Task<ItemCategoryDto> UpdateCategoryAsync(
        Guid id, UpdateItemCategoryRequest request, CancellationToken ct = default)
    {
        var category = await _db.ItemCategories
            .FirstOrDefaultAsync(c => c.Id == id && c.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(ItemCategory), id);

        if (request.ParentCategoryId == id)
            throw new BusinessRuleViolationException("A category cannot be its own parent.");

        if (request.ParentCategoryId.HasValue)
        {
            await EnsureCategoryAsync(request.ParentCategoryId.Value, ct);
            await EnsureNoCycleAsync(id, request.ParentCategoryId.Value, ct);
        }

        category.Name = request.Name;
        category.ParentCategoryId = request.ParentCategoryId;
        category.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return (await ListCategoriesAsync(ct)).First(c => c.Id == id);
    }

    /// <summary>Walks the parent chain so a category tree can never become circular.</summary>
    private async Task EnsureNoCycleAsync(Guid categoryId, Guid proposedParentId, CancellationToken ct)
    {
        var cursor = proposedParentId;
        var guard = 0;

        while (guard++ < 50)
        {
            if (cursor == categoryId)
                throw new BusinessRuleViolationException(
                    "That parent would create a circular category hierarchy.");

            var next = await _db.ItemCategories
                .Where(c => c.Id == cursor)
                .Select(c => c.ParentCategoryId)
                .FirstOrDefaultAsync(ct);

            if (next is null) return;
            cursor = next.Value;
        }

        throw new BusinessRuleViolationException("Category hierarchy is nested too deeply.");
    }

    // --- Units of measure (doc §4.3) -----------------------------------------

    public async Task<IReadOnlyList<UnitOfMeasureDto>> ListUomsAsync(CancellationToken ct = default)
        => await _db.UnitsOfMeasure
            .OrderBy(u => u.Code)
            .Select(u => new UnitOfMeasureDto(u.Id, u.Code, u.Name, u.Description, u.IsActive))
            .ToListAsync(ct);

    public async Task<UnitOfMeasureDto> CreateUomAsync(
        CreateUnitOfMeasureRequest request, CancellationToken ct = default)
    {
        if (await _db.UnitsOfMeasure.AnyAsync(u => u.Code == request.Code, ct))
            throw new DuplicateEntityException($"Unit of measure '{request.Code}' already exists.");

        var uom = new UnitOfMeasure
        {
            Code = request.Code,
            Name = request.Name,
            Description = request.Description,
            IsActive = true
        };

        _db.UnitsOfMeasure.Add(uom);
        await _db.SaveChangesAsync(ct);

        return new UnitOfMeasureDto(uom.Id, uom.Code, uom.Name, uom.Description, uom.IsActive);
    }

    // --- Attribute definitions (doc §4.2) ------------------------------------

    public async Task<IReadOnlyList<AttributeDefinitionDto>> ListAttributeDefinitionsAsync(
        CancellationToken ct = default)
        => await _db.AttributeDefinitions
            .Where(a => a.AccountId == _scope.AccountId)
            .OrderBy(a => a.Code)
            .Select(a => new AttributeDefinitionDto(
                a.Id, a.Code, a.Name, a.DataType,
                a.IsRequired, a.IsFilterable, a.IsSlottingRelevant, a.IsPickingRelevant, a.IsActive))
            .ToListAsync(ct);

    public async Task<AttributeDefinitionDto> CreateAttributeDefinitionAsync(
        CreateAttributeDefinitionRequest request, CancellationToken ct = default)
    {
        var accountId = _scope.AccountId;

        if (await _db.AttributeDefinitions.AnyAsync(a => a.AccountId == accountId && a.Code == request.Code, ct))
            throw new DuplicateEntityException($"Attribute code '{request.Code}' already exists.");

        var definition = new AttributeDefinition
        {
            AccountId = accountId,
            Code = request.Code,
            Name = request.Name,
            DataType = request.DataType,
            IsRequired = request.IsRequired,
            IsFilterable = request.IsFilterable,
            IsSlottingRelevant = request.IsSlottingRelevant,
            IsPickingRelevant = request.IsPickingRelevant,
            IsActive = true
        };

        _db.AttributeDefinitions.Add(definition);
        await _db.SaveChangesAsync(ct);

        return new AttributeDefinitionDto(
            definition.Id, definition.Code, definition.Name, definition.DataType,
            definition.IsRequired, definition.IsFilterable,
            definition.IsSlottingRelevant, definition.IsPickingRelevant, definition.IsActive);
    }

    public async Task<AttributeDefinitionDto> UpdateAttributeDefinitionAsync(
        Guid id, UpdateAttributeDefinitionRequest request, CancellationToken ct = default)
    {
        var definition = await _db.AttributeDefinitions
            .FirstOrDefaultAsync(a => a.Id == id && a.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(AttributeDefinition), id);

        // The data type is deliberately immutable: changing it would strand every value
        // already written to the previous type's column.
        definition.Name = request.Name;
        definition.IsRequired = request.IsRequired;
        definition.IsFilterable = request.IsFilterable;
        definition.IsSlottingRelevant = request.IsSlottingRelevant;
        definition.IsPickingRelevant = request.IsPickingRelevant;
        definition.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);

        return new AttributeDefinitionDto(
            definition.Id, definition.Code, definition.Name, definition.DataType,
            definition.IsRequired, definition.IsFilterable,
            definition.IsSlottingRelevant, definition.IsPickingRelevant, definition.IsActive);
    }

    // --- Suppliers -----------------------------------------------------------

    public async Task<IReadOnlyList<SupplierDto>> ListSuppliersAsync(CancellationToken ct = default)
        => await _db.Suppliers
            .Where(s => s.AccountId == _scope.AccountId)
            .OrderBy(s => s.Code)
            .Select(s => new SupplierDto(s.Id, s.Code, s.Name, s.ContactName,
                s.Email, s.Phone, s.Address, s.IsActive))
            .ToListAsync(ct);

    public async Task<SupplierDto> CreateSupplierAsync(
        CreateSupplierRequest request, CancellationToken ct = default)
    {
        var accountId = _scope.AccountId;

        if (await _db.Suppliers.AnyAsync(s => s.AccountId == accountId && s.Code == request.Code, ct))
            throw new DuplicateEntityException($"Supplier code '{request.Code}' already exists.");

        var supplier = new Supplier
        {
            AccountId = accountId,
            Code = request.Code,
            Name = request.Name,
            ContactName = request.ContactName,
            Email = request.Email,
            Phone = request.Phone,
            Address = request.Address,
            IsActive = true
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);

        return new SupplierDto(supplier.Id, supplier.Code, supplier.Name, supplier.ContactName,
            supplier.Email, supplier.Phone, supplier.Address, supplier.IsActive);
    }

    public async Task<SupplierDto> UpdateSupplierAsync(
        Guid id, UpdateSupplierRequest request, CancellationToken ct = default)
    {
        var supplier = await _db.Suppliers
            .FirstOrDefaultAsync(s => s.Id == id && s.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Supplier), id);

        supplier.Name = request.Name;
        supplier.ContactName = request.ContactName;
        supplier.Email = request.Email;
        supplier.Phone = request.Phone;
        supplier.Address = request.Address;
        supplier.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);

        return new SupplierDto(supplier.Id, supplier.Code, supplier.Name, supplier.ContactName,
            supplier.Email, supplier.Phone, supplier.Address, supplier.IsActive);
    }

    // --- Customers -----------------------------------------------------------

    public async Task<IReadOnlyList<CustomerDto>> ListCustomersAsync(CancellationToken ct = default)
        => await _db.Customers
            .Where(c => c.AccountId == _scope.AccountId)
            .OrderBy(c => c.Code)
            .Select(c => new CustomerDto(c.Id, c.Code, c.Name, c.ContactName,
                c.Email, c.Phone, c.ShippingAddress, c.IsActive))
            .ToListAsync(ct);

    public async Task<CustomerDto> CreateCustomerAsync(
        CreateCustomerRequest request, CancellationToken ct = default)
    {
        var accountId = _scope.AccountId;

        if (await _db.Customers.AnyAsync(c => c.AccountId == accountId && c.Code == request.Code, ct))
            throw new DuplicateEntityException($"Customer code '{request.Code}' already exists.");

        var customer = new Customer
        {
            AccountId = accountId,
            Code = request.Code,
            Name = request.Name,
            ContactName = request.ContactName,
            Email = request.Email,
            Phone = request.Phone,
            ShippingAddress = request.ShippingAddress,
            IsActive = true
        };

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);

        return new CustomerDto(customer.Id, customer.Code, customer.Name, customer.ContactName,
            customer.Email, customer.Phone, customer.ShippingAddress, customer.IsActive);
    }

    public async Task<CustomerDto> UpdateCustomerAsync(
        Guid id, UpdateCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Customer), id);

        customer.Name = request.Name;
        customer.ContactName = request.ContactName;
        customer.Email = request.Email;
        customer.Phone = request.Phone;
        customer.ShippingAddress = request.ShippingAddress;
        customer.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);

        return new CustomerDto(customer.Id, customer.Code, customer.Name, customer.ContactName,
            customer.Email, customer.Phone, customer.ShippingAddress, customer.IsActive);
    }

    private async Task EnsureCategoryAsync(Guid categoryId, CancellationToken ct)
    {
        var ok = await _db.ItemCategories
            .AnyAsync(c => c.Id == categoryId && c.AccountId == _scope.AccountId, ct);

        if (!ok) throw new NotFoundException(nameof(ItemCategory), categoryId);
    }
}
