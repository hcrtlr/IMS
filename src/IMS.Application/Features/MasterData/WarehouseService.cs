using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.MasterData;

/// <summary>
/// Doc §3.1 / §3.2 - Account and Warehouse CRUD.
/// Accounts are administered only by Admins; warehouses live inside the caller's account.
/// </summary>
public class WarehouseService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;

    public WarehouseService(IApplicationDbContext db, ScopeGuard scope)
    {
        _db = db;
        _scope = scope;
    }

    // --- Accounts (doc §3.1) -------------------------------------------------

    public async Task<IReadOnlyList<AccountDto>> ListAccountsAsync(CancellationToken ct = default)
    {
        // A caller only ever sees their own account; there is no cross-tenant listing.
        var accounts = await _db.Accounts
            .Where(a => a.Id == _scope.AccountId)
            .OrderBy(a => a.Code)
            .ToListAsync(ct);

        return accounts.Select(ToDto).ToList();
    }

    public async Task<AccountDto> GetAccountAsync(Guid id, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.Id == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Account), id);

        return ToDto(account);
    }

    public async Task<AccountDto> UpdateAccountAsync(Guid id, UpdateAccountRequest request, CancellationToken ct = default)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.Id == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Account), id);

        account.Name = request.Name;
        account.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return ToDto(account);
    }

    // --- Warehouses (doc §3.2) -----------------------------------------------

    public async Task<PagedResult<WarehouseDto>> ListWarehousesAsync(PagedQuery query, CancellationToken ct = default)
    {
        var q = _db.Warehouses.Where(w => w.AccountId == _scope.AccountId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(w => w.Code.ToLower().Contains(term) || w.Name.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(w => w.Code)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(w => new WarehouseDto(
                w.Id, w.AccountId, w.Code, w.Name, w.Address, w.TimeZone, w.IsActive,
                w.Zones.Count, w.Locations.Count))
            .ToListAsync(ct);

        return new PagedResult<WarehouseDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<WarehouseDto> GetWarehouseAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _db.Warehouses
            .Where(w => w.Id == id && w.AccountId == _scope.AccountId)
            .Select(w => new WarehouseDto(
                w.Id, w.AccountId, w.Code, w.Name, w.Address, w.TimeZone, w.IsActive,
                w.Zones.Count, w.Locations.Count))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Warehouse), id);

        return dto;
    }

    public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct = default)
    {
        var accountId = _scope.AccountId;

        if (await _db.Warehouses.AnyAsync(w => w.AccountId == accountId && w.Code == request.Code, ct))
            throw new DuplicateEntityException($"Warehouse code '{request.Code}' already exists in this account.");

        var warehouse = new Warehouse
        {
            AccountId = accountId,
            Code = request.Code,
            Name = request.Name,
            Address = request.Address,
            TimeZone = request.TimeZone,
            IsActive = true
        };

        _db.Warehouses.Add(warehouse);
        await _db.SaveChangesAsync(ct);

        return await GetWarehouseAsync(warehouse.Id, ct);
    }

    public async Task<WarehouseDto> UpdateWarehouseAsync(Guid id, UpdateWarehouseRequest request, CancellationToken ct = default)
    {
        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == id && w.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Warehouse), id);

        warehouse.Name = request.Name;
        warehouse.Address = request.Address;
        warehouse.TimeZone = request.TimeZone;
        warehouse.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return await GetWarehouseAsync(id, ct);
    }

    /// <summary>
    /// Deactivates rather than deletes: warehouses are referenced by immutable inventory
    /// transactions (§11.10), so a hard delete would break the audit trail.
    /// </summary>
    public async Task DeactivateWarehouseAsync(Guid id, CancellationToken ct = default)
    {
        var warehouse = await _db.Warehouses
            .FirstOrDefaultAsync(w => w.Id == id && w.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Warehouse), id);

        var hasStock = await _db.InventoryBalances
            .AnyAsync(b => b.WarehouseId == id && b.OnHandQuantity > 0, ct);

        if (hasStock)
            throw new BusinessRuleViolationException(
                "Cannot deactivate a warehouse that still holds stock.");

        warehouse.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    private static AccountDto ToDto(Account a)
        => new(a.Id, a.Code, a.Name, a.IsActive, a.CreatedAt, a.UpdatedAt);
}
