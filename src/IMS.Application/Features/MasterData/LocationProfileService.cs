using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.MasterData;

/// <summary>
/// Doc §3.5 - shared rule-sets for similar locations. These carry the capacity and
/// temperature constraints that business rules §11.7 and §11.8 are checked against.
/// </summary>
public class LocationProfileService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;

    public LocationProfileService(IApplicationDbContext db, ScopeGuard scope)
    {
        _db = db;
        _scope = scope;
    }

    private IQueryable<LocationProfile> Scoped()
        => _db.LocationProfiles.Where(p => p.AccountId == _scope.AccountId);

    public async Task<IReadOnlyList<LocationProfileDto>> ListAsync(CancellationToken ct = default)
        => await Scoped()
            .OrderBy(p => p.Code)
            .Select(p => new LocationProfileDto(
                p.Id, p.Code, p.Name, p.LocationType, p.MaxWeight, p.MaxVolume,
                p.AllowedItemCategoryId, p.AllowedItemCategory != null ? p.AllowedItemCategory.Name : null,
                p.TemperatureMin, p.TemperatureMax,
                p.IsMixedItemAllowed, p.IsMixedLotAllowed, p.IsActive))
            .ToListAsync(ct);

    public async Task<LocationProfileDto> GetAsync(Guid id, CancellationToken ct = default)
        => await Scoped()
            .Where(p => p.Id == id)
            .Select(p => new LocationProfileDto(
                p.Id, p.Code, p.Name, p.LocationType, p.MaxWeight, p.MaxVolume,
                p.AllowedItemCategoryId, p.AllowedItemCategory != null ? p.AllowedItemCategory.Name : null,
                p.TemperatureMin, p.TemperatureMax,
                p.IsMixedItemAllowed, p.IsMixedLotAllowed, p.IsActive))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(LocationProfile), id);

    public async Task<LocationProfileDto> CreateAsync(
        CreateLocationProfileRequest request, CancellationToken ct = default)
    {
        var accountId = _scope.AccountId;

        if (await _db.LocationProfiles.AnyAsync(p => p.AccountId == accountId && p.Code == request.Code, ct))
            throw new DuplicateEntityException($"Location profile code '{request.Code}' already exists.");

        ValidateTemperatureBand(request.TemperatureMin, request.TemperatureMax);

        if (request.AllowedItemCategoryId.HasValue)
            await EnsureCategoryAsync(request.AllowedItemCategoryId.Value, ct);

        var profile = new LocationProfile
        {
            AccountId = accountId,
            Code = request.Code,
            Name = request.Name,
            LocationType = request.LocationType,
            MaxWeight = request.MaxWeight,
            MaxVolume = request.MaxVolume,
            AllowedItemCategoryId = request.AllowedItemCategoryId,
            TemperatureMin = request.TemperatureMin,
            TemperatureMax = request.TemperatureMax,
            IsMixedItemAllowed = request.IsMixedItemAllowed,
            IsMixedLotAllowed = request.IsMixedLotAllowed,
            IsActive = true
        };

        _db.LocationProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(profile.Id, ct);
    }

    public async Task<LocationProfileDto> UpdateAsync(
        Guid id, UpdateLocationProfileRequest request, CancellationToken ct = default)
    {
        var profile = await Scoped().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(nameof(LocationProfile), id);

        ValidateTemperatureBand(request.TemperatureMin, request.TemperatureMax);

        if (request.AllowedItemCategoryId.HasValue)
            await EnsureCategoryAsync(request.AllowedItemCategoryId.Value, ct);

        profile.Name = request.Name;
        profile.LocationType = request.LocationType;
        profile.MaxWeight = request.MaxWeight;
        profile.MaxVolume = request.MaxVolume;
        profile.AllowedItemCategoryId = request.AllowedItemCategoryId;
        profile.TemperatureMin = request.TemperatureMin;
        profile.TemperatureMax = request.TemperatureMax;
        profile.IsMixedItemAllowed = request.IsMixedItemAllowed;
        profile.IsMixedLotAllowed = request.IsMixedLotAllowed;
        profile.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static void ValidateTemperatureBand(decimal? min, decimal? max)
    {
        if (min.HasValue && max.HasValue && min > max)
            throw new BusinessRuleViolationException(
                "TemperatureMin cannot be greater than TemperatureMax.");
    }

    private async Task EnsureCategoryAsync(Guid categoryId, CancellationToken ct)
    {
        var ok = await _db.ItemCategories
            .AnyAsync(c => c.Id == categoryId && c.AccountId == _scope.AccountId, ct);

        if (!ok) throw new NotFoundException(nameof(ItemCategory), categoryId);
    }
}
