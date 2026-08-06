using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.MasterData;

/// <summary>Doc §3.3 - zones inside a warehouse.</summary>
public class ZoneService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;

    public ZoneService(IApplicationDbContext db, ScopeGuard scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<IReadOnlyList<ZoneDto>> ListAsync(
        Guid? warehouseId, ZoneType? zoneType, CancellationToken ct = default)
    {
        var q = _db.Zones.Where(z => z.Warehouse.AccountId == _scope.AccountId);

        if (warehouseId.HasValue) q = q.Where(z => z.WarehouseId == warehouseId.Value);
        if (zoneType.HasValue) q = q.Where(z => z.ZoneType == zoneType.Value);

        return await q
            .OrderBy(z => z.Warehouse.Code).ThenBy(z => z.Priority).ThenBy(z => z.Code)
            .Select(z => new ZoneDto(
                z.Id, z.WarehouseId, z.Warehouse.Code, z.Code, z.Name,
                z.ZoneType, z.Priority, z.IsActive, z.Locations.Count))
            .ToListAsync(ct);
    }

    public async Task<ZoneDto> GetAsync(Guid id, CancellationToken ct = default)
        => await _db.Zones
            .Where(z => z.Id == id && z.Warehouse.AccountId == _scope.AccountId)
            .Select(z => new ZoneDto(
                z.Id, z.WarehouseId, z.Warehouse.Code, z.Code, z.Name,
                z.ZoneType, z.Priority, z.IsActive, z.Locations.Count))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Zone), id);

    public async Task<ZoneDto> CreateAsync(CreateZoneRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);

        if (await _db.Zones.AnyAsync(z => z.WarehouseId == request.WarehouseId && z.Code == request.Code, ct))
            throw new DuplicateEntityException(
                $"Zone code '{request.Code}' already exists in this warehouse.");

        var zone = new Zone
        {
            WarehouseId = request.WarehouseId,
            Code = request.Code,
            Name = request.Name,
            ZoneType = request.ZoneType,
            Priority = request.Priority,
            IsActive = true
        };

        _db.Zones.Add(zone);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(zone.Id, ct);
    }

    public async Task<ZoneDto> UpdateAsync(Guid id, UpdateZoneRequest request, CancellationToken ct = default)
    {
        var zone = await _db.Zones
            .FirstOrDefaultAsync(z => z.Id == id && z.Warehouse.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Zone), id);

        // Changing the type of a zone that already holds stock would silently invalidate
        // the putaway compatibility decisions already made against it (rule §11.8).
        if (zone.ZoneType != request.ZoneType)
        {
            var hasStock = await _db.InventoryBalances
                .AnyAsync(b => b.Location.ZoneId == id && b.OnHandQuantity > 0, ct);

            if (hasStock)
                throw new BusinessRuleViolationException(
                    "Cannot change the type of a zone whose locations currently hold stock.", ruleNumber: 8);
        }

        zone.Name = request.Name;
        zone.ZoneType = request.ZoneType;
        zone.Priority = request.Priority;
        zone.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var zone = await _db.Zones
            .FirstOrDefaultAsync(z => z.Id == id && z.Warehouse.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Zone), id);

        var hasStock = await _db.InventoryBalances
            .AnyAsync(b => b.Location.ZoneId == id && b.OnHandQuantity > 0, ct);

        if (hasStock)
            throw new BusinessRuleViolationException("Cannot deactivate a zone that still holds stock.");

        zone.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }
}
