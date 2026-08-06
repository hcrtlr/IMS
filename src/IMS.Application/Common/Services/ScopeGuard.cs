using IMS.Application.Common.Interfaces;
using IMS.Application.Features.Auth;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Common.Services;

/// <summary>
/// Central tenant-isolation guard.
///
/// Doc §3 recommends carrying AccountId and WarehouseId on every table. Account-scoped
/// entities are filtered directly on AccountId; warehouse-scoped entities (Zone,
/// Location, InventoryBalance, ...) reach their account through Warehouse, so every
/// service resolves the caller's account here and validates any warehouse id supplied
/// by the client before touching data.
///
/// Without this, a caller could pass another tenant's WarehouseId and read or move
/// their stock.
/// </summary>
public class ScopeGuard
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ScopeGuard(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>The caller's account id, or an authentication error if the token lacks one.</summary>
    public Guid AccountId => _currentUser.AccountId
        ?? throw new AuthenticationFailedException("The current token carries no account claim.");

    public string? Username => _currentUser.Username;

    /// <summary>
    /// Confirms the warehouse exists and belongs to the caller's account.
    /// Reports NotFound rather than Forbidden so cross-tenant probing cannot confirm
    /// that an id exists elsewhere.
    /// </summary>
    public async Task EnsureWarehouseAsync(Guid warehouseId, CancellationToken ct = default)
    {
        var ok = await _db.Warehouses
            .AnyAsync(w => w.Id == warehouseId && w.AccountId == AccountId, ct);

        if (!ok) throw new NotFoundException($"Warehouse '{warehouseId}' was not found.");
    }

    /// <summary>Confirms a location exists inside a warehouse owned by the caller's account.</summary>
    public async Task EnsureLocationAsync(Guid locationId, Guid warehouseId, CancellationToken ct = default)
    {
        var ok = await _db.Locations
            .AnyAsync(l => l.Id == locationId
                           && l.WarehouseId == warehouseId
                           && l.Warehouse.AccountId == AccountId, ct);

        if (!ok) throw new NotFoundException($"Location '{locationId}' was not found in warehouse '{warehouseId}'.");
    }

    /// <summary>Confirms an item exists in the caller's account.</summary>
    public async Task EnsureItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var ok = await _db.Items.AnyAsync(i => i.Id == itemId && i.AccountId == AccountId, ct);
        if (!ok) throw new NotFoundException($"Item '{itemId}' was not found.");
    }
}
