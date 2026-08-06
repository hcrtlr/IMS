using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.MasterData;

/// <summary>
/// Doc §3.4 / §3.5 - locations and location profiles, including the §10 fields that
/// future slotting and routing algorithms depend on.
/// </summary>
public class LocationService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;

    public LocationService(IApplicationDbContext db, ScopeGuard scope)
    {
        _db = db;
        _scope = scope;
    }

    private IQueryable<Location> Scoped()
        => _db.Locations.Where(l => l.Warehouse.AccountId == _scope.AccountId);

    public async Task<PagedResult<LocationDto>> ListAsync(
        Guid? warehouseId, Guid? zoneId, PagedQuery query, CancellationToken ct = default)
    {
        var q = Scoped();

        if (warehouseId.HasValue) q = q.Where(l => l.WarehouseId == warehouseId.Value);
        if (zoneId.HasValue) q = q.Where(l => l.ZoneId == zoneId.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(l => l.Code.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);

        var rows = await q
            .OrderBy(l => l.Code)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(l => new
            {
                Location = l,
                ZoneCode = l.Zone.Code,
                ZoneType = l.Zone.ZoneType,
                ProfileCode = l.LocationProfile != null ? l.LocationProfile.Code : null,
                OnHand = _db.InventoryBalances
                    .Where(b => b.LocationId == l.Id)
                    .Sum(b => (decimal?)b.OnHandQuantity) ?? 0m,
                DistinctItems = _db.InventoryBalances
                    .Where(b => b.LocationId == l.Id && b.OnHandQuantity > 0)
                    .Select(b => b.ItemId).Distinct().Count()
            })
            .ToListAsync(ct);

        var items = rows.Select(r => Map(r.Location, r.ZoneCode, r.ZoneType, r.ProfileCode, r.OnHand, r.DistinctItems))
            .ToList();

        return new PagedResult<LocationDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<LocationDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await Scoped()
            .Where(l => l.Id == id)
            .Select(l => new
            {
                Location = l,
                ZoneCode = l.Zone.Code,
                ZoneType = l.Zone.ZoneType,
                ProfileCode = l.LocationProfile != null ? l.LocationProfile.Code : null,
                OnHand = _db.InventoryBalances
                    .Where(b => b.LocationId == l.Id)
                    .Sum(b => (decimal?)b.OnHandQuantity) ?? 0m,
                DistinctItems = _db.InventoryBalances
                    .Where(b => b.LocationId == l.Id && b.OnHandQuantity > 0)
                    .Select(b => b.ItemId).Distinct().Count()
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Location), id);

        return Map(row.Location, row.ZoneCode, row.ZoneType, row.ProfileCode, row.OnHand, row.DistinctItems);
    }

    /// <summary>
    /// GET /api/locations/available (§12). Returns locations that can legally accept
    /// putaway, optionally filtered for compatibility with a specific item so callers
    /// see only candidates that satisfy rule §11.8 (temperature / hazmat) and the
    /// profile's mixing rules.
    /// </summary>
    public async Task<IReadOnlyList<LocationDto>> GetAvailableAsync(
        AvailableLocationQuery query, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(query.WarehouseId, ct);

        var q = Scoped().Where(l => l.WarehouseId == query.WarehouseId && l.IsActive);

        if (query.PutawayOnly) q = q.Where(l => l.IsPutawayAllowed);
        if (query.ZoneId.HasValue) q = q.Where(l => l.ZoneId == query.ZoneId.Value);
        if (query.ZoneType.HasValue) q = q.Where(l => l.Zone.ZoneType == query.ZoneType.Value);
        if (query.LocationType.HasValue) q = q.Where(l => l.LocationType == query.LocationType.Value);

        if (query.EmptyOnly)
            q = q.Where(l => !_db.InventoryBalances.Any(b => b.LocationId == l.Id && b.OnHandQuantity > 0));

        ItemMaster? item = null;
        if (query.ItemId.HasValue)
        {
            item = await _db.Items
                .FirstOrDefaultAsync(i => i.Id == query.ItemId.Value && i.AccountId == _scope.AccountId, ct)
                ?? throw new NotFoundException(nameof(ItemMaster), query.ItemId.Value);

            // Hazardous stock only goes to hazmat-capable profiles/zones (rule §11.8).
            if (item.IsHazardous)
            {
                q = q.Where(l =>
                    l.LocationType == Domain.Enums.LocationType.DangerousGoods ||
                    l.Zone.ZoneType == Domain.Enums.ZoneType.HazardousMaterial);
            }

            // Temperature-controlled stock only goes to profiles declaring a band.
            if (item.IsTemperatureControlled)
            {
                q = q.Where(l => l.LocationProfile != null
                                 && (l.LocationProfile.TemperatureMin != null
                                     || l.LocationProfile.TemperatureMax != null));
            }

            // Respect a profile that restricts which category it accepts.
            if (item.CategoryId.HasValue)
            {
                var categoryId = item.CategoryId.Value;
                q = q.Where(l => l.LocationProfile == null
                                 || l.LocationProfile.AllowedItemCategoryId == null
                                 || l.LocationProfile.AllowedItemCategoryId == categoryId);
            }

            // Profiles that forbid mixing must not already hold a different item.
            var itemId = item.Id;
            q = q.Where(l => l.LocationProfile == null
                             || l.LocationProfile.IsMixedItemAllowed
                             || !_db.InventoryBalances.Any(b =>
                                 b.LocationId == l.Id && b.OnHandQuantity > 0 && b.ItemId != itemId));
        }

        var rows = await q
            // Putaway sequence first so the result already reflects the intended walk order.
            .OrderBy(l => l.PutawaySequence ?? int.MaxValue).ThenBy(l => l.Code)
            .Take(query.Limit)
            .Select(l => new
            {
                Location = l,
                ZoneCode = l.Zone.Code,
                ZoneType = l.Zone.ZoneType,
                ProfileCode = l.LocationProfile != null ? l.LocationProfile.Code : null,
                ProfileTempMin = l.LocationProfile != null ? l.LocationProfile.TemperatureMin : null,
                ProfileTempMax = l.LocationProfile != null ? l.LocationProfile.TemperatureMax : null,
                OnHand = _db.InventoryBalances
                    .Where(b => b.LocationId == l.Id).Sum(b => (decimal?)b.OnHandQuantity) ?? 0m,
                DistinctItems = _db.InventoryBalances
                    .Where(b => b.LocationId == l.Id && b.OnHandQuantity > 0)
                    .Select(b => b.ItemId).Distinct().Count()
            })
            .ToListAsync(ct);

        // The temperature band comparison needs both ends, which is clearer in memory
        // than as a SQL expression.
        if (item?.IsTemperatureControlled == true)
        {
            rows = rows.Where(r =>
                new LocationProfile
                {
                    TemperatureMin = r.ProfileTempMin,
                    TemperatureMax = r.ProfileTempMax
                }.SupportsTemperatureRange(
                    item.MinimumStorageTemperature, item.MaximumStorageTemperature))
                .ToList();
        }

        return rows
            .Select(r => Map(r.Location, r.ZoneCode, r.ZoneType, r.ProfileCode, r.OnHand, r.DistinctItems))
            .ToList();
    }

    public async Task<LocationDto> CreateAsync(CreateLocationRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);

        var zoneOk = await _db.Zones.AnyAsync(
            z => z.Id == request.ZoneId && z.WarehouseId == request.WarehouseId, ct);

        if (!zoneOk)
            throw new NotFoundException($"Zone '{request.ZoneId}' was not found in warehouse '{request.WarehouseId}'.");

        if (await _db.Locations.AnyAsync(l => l.WarehouseId == request.WarehouseId && l.Code == request.Code, ct))
            throw new DuplicateEntityException($"Location code '{request.Code}' already exists in this warehouse.");

        if (request.LocationProfileId.HasValue)
            await EnsureProfileAsync(request.LocationProfileId.Value, ct);

        var location = new Location
        {
            WarehouseId = request.WarehouseId,
            ZoneId = request.ZoneId,
            Code = request.Code,
            Aisle = request.Aisle,
            Bay = request.Bay,
            Level = request.Level,
            Position = request.Position,
            LocationType = request.LocationType,
            LocationProfileId = request.LocationProfileId,
            PickSequence = request.PickSequence,
            PutawaySequence = request.PutawaySequence,
            CoordinateX = request.CoordinateX,
            CoordinateY = request.CoordinateY,
            CoordinateZ = request.CoordinateZ,
            MaxWeight = request.MaxWeight,
            MaxVolume = request.MaxVolume,
            IsPickable = request.IsPickable,
            IsPutawayAllowed = request.IsPutawayAllowed,
            DistanceToReceiving = request.DistanceToReceiving,
            DistanceToPacking = request.DistanceToPacking,
            DistanceToShipping = request.DistanceToShipping,
            AccessibilityScore = request.AccessibilityScore,
            MaxConcurrentWorkers = request.MaxConcurrentWorkers,
            IsActive = true
        };

        _db.Locations.Add(location);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(location.Id, ct);
    }

    public async Task<LocationDto> UpdateAsync(Guid id, UpdateLocationRequest request, CancellationToken ct = default)
    {
        var location = await Scoped().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException(nameof(Location), id);

        var zoneOk = await _db.Zones.AnyAsync(
            z => z.Id == request.ZoneId && z.WarehouseId == location.WarehouseId, ct);

        if (!zoneOk)
            throw new NotFoundException($"Zone '{request.ZoneId}' was not found in this warehouse.");

        if (request.LocationProfileId.HasValue)
            await EnsureProfileAsync(request.LocationProfileId.Value, ct);

        location.ZoneId = request.ZoneId;
        location.Aisle = request.Aisle;
        location.Bay = request.Bay;
        location.Level = request.Level;
        location.Position = request.Position;
        location.LocationType = request.LocationType;
        location.LocationProfileId = request.LocationProfileId;
        location.PickSequence = request.PickSequence;
        location.PutawaySequence = request.PutawaySequence;
        location.CoordinateX = request.CoordinateX;
        location.CoordinateY = request.CoordinateY;
        location.CoordinateZ = request.CoordinateZ;
        location.MaxWeight = request.MaxWeight;
        location.MaxVolume = request.MaxVolume;
        location.IsPickable = request.IsPickable;
        location.IsPutawayAllowed = request.IsPutawayAllowed;
        location.IsActive = request.IsActive;
        location.DistanceToReceiving = request.DistanceToReceiving;
        location.DistanceToPacking = request.DistanceToPacking;
        location.DistanceToShipping = request.DistanceToShipping;
        location.AccessibilityScore = request.AccessibilityScore;
        location.MaxConcurrentWorkers = request.MaxConcurrentWorkers;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var location = await Scoped().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException(nameof(Location), id);

        var hasStock = await _db.InventoryBalances.AnyAsync(b => b.LocationId == id && b.OnHandQuantity > 0, ct);
        if (hasStock)
            throw new BusinessRuleViolationException("Cannot deactivate a location that still holds stock.");

        location.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureProfileAsync(Guid profileId, CancellationToken ct)
    {
        var ok = await _db.LocationProfiles
            .AnyAsync(p => p.Id == profileId && p.AccountId == _scope.AccountId, ct);

        if (!ok) throw new NotFoundException(nameof(LocationProfile), profileId);
    }

    private static LocationDto Map(
        Location l, string zoneCode, Domain.Enums.ZoneType zoneType,
        string? profileCode, decimal onHand, int distinctItems)
        => new(
            l.Id, l.WarehouseId, l.ZoneId, zoneCode, zoneType,
            l.Code, l.Aisle, l.Bay, l.Level, l.Position,
            l.LocationType, l.LocationProfileId, profileCode,
            l.PickSequence, l.PutawaySequence,
            l.CoordinateX, l.CoordinateY, l.CoordinateZ,
            l.MaxWeight, l.MaxVolume,
            l.IsPickable, l.IsPutawayAllowed, l.IsActive,
            l.DistanceToReceiving, l.DistanceToPacking, l.DistanceToShipping,
            l.AccessibilityScore, l.MaxConcurrentWorkers,
            onHand, distinctItems);
}
