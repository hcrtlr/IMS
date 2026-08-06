using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.Algorithms;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.Algorithms;

/// <summary>
/// Faz 6 - the groundwork for slotting, putaway, picking-routing and order batching.
///
/// Doc §10 is explicit that the algorithms themselves are out of scope for v1, and that
/// what matters is capturing the data they will need. This service therefore:
///   - manages tunable configuration a future engine will read;
///   - lets an engine record its recommendations and plans, and captures whether an
///     operator accepted them, so recommendation quality can be measured later;
///   - exposes the demand history and readiness of the §10 data points.
///
/// Nothing here makes a slotting or routing decision.
/// </summary>
public class AlgorithmReadinessService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;

    public AlgorithmReadinessService(
        IApplicationDbContext db, ScopeGuard scope, IDateTimeProvider clock)
    {
        _db = db;
        _scope = scope;
        _clock = clock;
    }

    // ------------------------------------------------------- configuration

    public async Task<IReadOnlyList<AlgorithmConfigurationDto>> ListConfigurationsAsync(
        string? algorithmName, Guid? warehouseId, CancellationToken ct = default)
    {
        var q = _db.AlgorithmConfigurations.Where(c => c.AccountId == _scope.AccountId);

        if (!string.IsNullOrWhiteSpace(algorithmName))
            q = q.Where(c => c.AlgorithmName == algorithmName);

        if (warehouseId.HasValue)
            q = q.Where(c => c.WarehouseId == warehouseId.Value || c.WarehouseId == null);

        return await q
            .OrderBy(c => c.AlgorithmName).ThenByDescending(c => c.Priority).ThenBy(c => c.ParameterKey)
            .Select(c => new AlgorithmConfigurationDto(
                c.Id, c.WarehouseId, c.AlgorithmName, c.ParameterKey, c.ParameterValue,
                c.ValueType, c.Description, c.IsActive, c.Version, c.Priority,
                c.CreatedBy, c.CreatedAt, c.UpdatedBy, c.UpdatedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Creates or updates one parameter. Updating bumps Version so a future engine can
    /// tell that its tuning changed without diffing values.
    /// </summary>
    public async Task<AlgorithmConfigurationDto> UpsertConfigurationAsync(
        UpsertAlgorithmConfigurationRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AlgorithmName))
            throw new BusinessRuleViolationException("AlgorithmName is required.");

        if (string.IsNullOrWhiteSpace(request.ParameterKey))
            throw new BusinessRuleViolationException("ParameterKey is required.");

        if (request.WarehouseId.HasValue)
            await _scope.EnsureWarehouseAsync(request.WarehouseId.Value, ct);

        var valueType = string.IsNullOrWhiteSpace(request.ValueType) ? "string" : request.ValueType;

        ValidateValue(request.ParameterValue, valueType);

        var existing = await _db.AlgorithmConfigurations.FirstOrDefaultAsync(
            c => c.AccountId == _scope.AccountId
                 && c.WarehouseId == request.WarehouseId
                 && c.AlgorithmName == request.AlgorithmName
                 && c.ParameterKey == request.ParameterKey, ct);

        if (existing is null)
        {
            existing = new AlgorithmConfiguration
            {
                AccountId = _scope.AccountId,
                WarehouseId = request.WarehouseId,
                AlgorithmName = request.AlgorithmName,
                ParameterKey = request.ParameterKey,
                ParameterValue = request.ParameterValue,
                ValueType = valueType,
                Description = request.Description,
                Priority = request.Priority ?? 0,
                IsActive = true,
                Version = 1
            };

            _db.AlgorithmConfigurations.Add(existing);
        }
        else
        {
            existing.ParameterValue = request.ParameterValue;
            existing.ValueType = valueType;
            existing.Description = request.Description;
            existing.Priority = request.Priority ?? existing.Priority;
            existing.Version++;
        }

        await _db.SaveChangesAsync(ct);

        return (await ListConfigurationsAsync(request.AlgorithmName, request.WarehouseId, ct))
            .First(c => c.Id == existing.Id);
    }

    /// <summary>A value that cannot be parsed as its declared type would fail at read time.</summary>
    private static void ValidateValue(string value, string valueType)
    {
        var ok = valueType.ToLowerInvariant() switch
        {
            "int" => int.TryParse(value, out _),
            "decimal" => decimal.TryParse(value, out _),
            "bool" => bool.TryParse(value, out _),
            "json" => value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('['),
            _ => true
        };

        if (!ok)
            throw new BusinessRuleViolationException(
                $"Value '{value}' is not a valid {valueType}.");
    }

    // -------------------------------------------- slotting recommendations

    public async Task<SlottingRecommendationDto> RecordRecommendationAsync(
        CreateSlottingRecommendationRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);
        await _scope.EnsureItemAsync(request.ItemId, ct);
        await _scope.EnsureLocationAsync(request.RecommendedLocationId, request.WarehouseId, ct);

        if (request.CurrentLocationId.HasValue)
            await _scope.EnsureLocationAsync(request.CurrentLocationId.Value, request.WarehouseId, ct);

        var recommendation = new SlottingRecommendation
        {
            WarehouseId = request.WarehouseId,
            ItemId = request.ItemId,
            CurrentLocationId = request.CurrentLocationId,
            RecommendedLocationId = request.RecommendedLocationId,
            Score = request.Score,
            RecommendationReason = request.RecommendationReason,
            AlgorithmName = request.AlgorithmName,
            AlgorithmVersion = request.AlgorithmVersion,
            GeneratedAt = _clock.UtcNow
        };

        _db.SlottingRecommendations.Add(recommendation);
        await _db.SaveChangesAsync(ct);

        return await GetRecommendationAsync(recommendation.Id, ct);
    }

    /// <summary>
    /// Records whether an operator accepted the suggestion. This acceptance rate is how a
    /// future slotting engine's quality gets measured against reality.
    /// </summary>
    public async Task<SlottingRecommendationDto> DecideRecommendationAsync(
        Guid id, DecideSlottingRecommendationRequest request, CancellationToken ct = default)
    {
        var recommendation = await _db.SlottingRecommendations
            .FirstOrDefaultAsync(r => r.Id == id
                && _db.Warehouses.Any(w => w.Id == r.WarehouseId && w.AccountId == _scope.AccountId), ct)
            ?? throw new NotFoundException(nameof(SlottingRecommendation), id);

        if (recommendation.WasAccepted.HasValue)
            throw new BusinessRuleViolationException("This recommendation has already been decided.");

        recommendation.WasAccepted = request.Accepted;
        recommendation.DecidedAt = _clock.UtcNow;
        recommendation.DecidedBy = _scope.Username;

        await _db.SaveChangesAsync(ct);
        return await GetRecommendationAsync(id, ct);
    }

    public async Task<SlottingRecommendationDto> GetRecommendationAsync(
        Guid id, CancellationToken ct = default)
        // Filter on the ENTITY before projecting: EF cannot translate a Where applied
        // to an already-projected DTO.
        => await ScopedRecommendations()
               .Where(r => r.Id == id)
               .Select(RecommendationProjection)
               .FirstOrDefaultAsync(ct)
           ?? throw new NotFoundException(nameof(SlottingRecommendation), id);

    public async Task<IReadOnlyList<SlottingRecommendationDto>> ListRecommendationsAsync(
        Guid? warehouseId, Guid? itemId, CancellationToken ct = default)
    {
        var q = ScopedRecommendations();

        if (warehouseId.HasValue) q = q.Where(r => r.WarehouseId == warehouseId.Value);
        if (itemId.HasValue) q = q.Where(r => r.ItemId == itemId.Value);

        return await q
            .OrderByDescending(r => r.GeneratedAt)
            .Select(RecommendationProjection)
            .ToListAsync(ct);
    }

    /// <summary>Account-scoped recommendations as ENTITIES, so filters stay translatable.</summary>
    private IQueryable<SlottingRecommendation> ScopedRecommendations()
        => _db.SlottingRecommendations
            .Where(r => _db.Warehouses.Any(w => w.Id == r.WarehouseId && w.AccountId == _scope.AccountId));

    // ---------------------------------------------------- picking plans

    public async Task<PickingPlanDto> RecordPickingPlanAsync(
        CreatePickingPlanRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);

        if (request.Stops is null || request.Stops.Count == 0)
            throw new BusinessRuleViolationException("A picking plan must have at least one stop.");

        var duplicated = request.Stops.GroupBy(s => s.StopSequence).Any(g => g.Count() > 1);
        if (duplicated)
            throw new BusinessRuleViolationException("Stop sequence numbers must be unique within a plan.");

        var planNumber = string.IsNullOrWhiteSpace(request.PlanNumber)
            ? $"PP-{_clock.UtcNow:yyyyMMddHHmmssfff}"
            : request.PlanNumber;

        if (await _db.PickingPlans.AnyAsync(p => p.PlanNumber == planNumber, ct))
            throw new DuplicateEntityException($"Picking plan '{planNumber}' already exists.");

        var plan = new PickingPlan
        {
            WarehouseId = request.WarehouseId,
            PlanNumber = planNumber,
            AlgorithmName = request.AlgorithmName,
            AlgorithmVersion = request.AlgorithmVersion,
            BatchingStrategy = request.BatchingStrategy,
            GeneratedAt = _clock.UtcNow,
            TotalStops = request.Stops.Count,
            EstimatedTravelDistance = request.EstimatedTravelDistance,
            EstimatedDurationSeconds = request.EstimatedDurationSeconds,
            AssignedTo = request.AssignedTo
        };

        foreach (var stop in request.Stops.OrderBy(s => s.StopSequence))
        {
            await _scope.EnsureLocationAsync(stop.LocationId, request.WarehouseId, ct);

            plan.Stops.Add(new PickingRouteStop
            {
                StopSequence = stop.StopSequence,
                LocationId = stop.LocationId,
                PickTaskId = stop.PickTaskId,
                ItemId = stop.ItemId,
                Quantity = stop.Quantity,
                DistanceFromPrevious = stop.DistanceFromPrevious
            });
        }

        // The whole graph is new, so adding the root marks every stop Added too.
        _db.PickingPlans.Add(plan);
        await _db.SaveChangesAsync(ct);

        return await GetPickingPlanAsync(plan.Id, ct);
    }

    public async Task<PickingPlanDto> GetPickingPlanAsync(Guid id, CancellationToken ct = default)
    {
        var plan = await _db.PickingPlans
            .Include(p => p.Stops).ThenInclude(s => s.Location)
            .FirstOrDefaultAsync(p => p.Id == id
                && _db.Warehouses.Any(w => w.Id == p.WarehouseId && w.AccountId == _scope.AccountId), ct)
            ?? throw new NotFoundException(nameof(PickingPlan), id);

        return new PickingPlanDto(
            plan.Id, plan.WarehouseId, plan.PlanNumber,
            plan.AlgorithmName, plan.AlgorithmVersion, plan.BatchingStrategy,
            plan.GeneratedAt, plan.TotalStops,
            plan.EstimatedTravelDistance, plan.ActualTravelDistance,
            plan.EstimatedDurationSeconds, plan.ActualDurationSeconds,
            plan.AssignedTo, plan.StartedAt, plan.CompletedAt,
            plan.Stops.OrderBy(s => s.StopSequence).Select(s => new PickingRouteStopDto(
                s.Id, s.StopSequence, s.LocationId, s.Location.Code,
                s.PickTaskId, s.ItemId, s.Quantity, s.DistanceFromPrevious,
                s.ArrivedAt, s.DepartedAt)).ToList());
    }

    // ---------------------------------------------------- order history

    public async Task<PagedResult<OrderHistoryDto>> ListOrderHistoryAsync(
        OrderHistoryQuery query, CancellationToken ct = default)
    {
        var q = _db.OrderHistory.Where(h => h.AccountId == _scope.AccountId);

        if (query.WarehouseId.HasValue) q = q.Where(h => h.WarehouseId == query.WarehouseId.Value);
        if (query.ItemId.HasValue) q = q.Where(h => h.ItemId == query.ItemId.Value);
        if (query.CustomerId.HasValue) q = q.Where(h => h.CustomerId == query.CustomerId.Value);
        if (query.FromDate.HasValue) q = q.Where(h => h.ShippedAt >= query.FromDate.Value);
        if (query.ToDate.HasValue) q = q.Where(h => h.ShippedAt <= query.ToDate.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(h => h.ShippedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(h => new OrderHistoryDto(
                h.Id, h.OrderId, h.OrderNumber, h.OrderDetailId,
                h.ItemId, h.Sku, h.CustomerId, h.OrderType, h.Priority,
                h.Carrier, h.ServiceLevel,
                h.OrderDate, h.RequiredShipDate, h.ShippedAt,
                h.OrderedQuantity, h.ShippedQuantity,
                h.OrderLineCount, h.PickedFromLocationId, h.FulfillmentDurationSeconds))
            .ToListAsync(ct);

        return new PagedResult<OrderHistoryDto>(items, total, query.Page, query.PageSize);
    }

    /// <summary>
    /// Per-item demand velocity from the shipped history - the primary input a slotting
    /// engine needs. Computed on demand; no algorithm consumes it yet.
    /// </summary>
    public async Task<IReadOnlyList<ItemDemandStatsDto>> GetDemandStatsAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(warehouseId, ct);

        var rows = await _db.OrderHistory
            .Where(h => h.AccountId == _scope.AccountId && h.WarehouseId == warehouseId)
            .GroupBy(h => new { h.ItemId, h.Sku })
            .Select(g => new
            {
                g.Key.ItemId,
                g.Key.Sku,
                Lines = g.Count(),
                TotalShipped = g.Sum(x => x.ShippedQuantity),
                First = g.Min(x => x.ShippedAt),
                Last = g.Max(x => x.ShippedAt)
            })
            .ToListAsync(ct);

        var orderCounts = await _db.OrderHistory
            .Where(h => h.AccountId == _scope.AccountId && h.WarehouseId == warehouseId)
            .Select(h => new { h.ItemId, h.OrderId })
            .Distinct()
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        var names = await _db.Items
            .Where(i => rows.Select(r => r.ItemId).Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Name, ct);

        return rows
            .Select(r =>
            {
                // At least one day, so a single day's shipping does not divide by zero.
                var days = Math.Max(1d, (r.Last - r.First).TotalDays);

                return new ItemDemandStatsDto(
                    r.ItemId, r.Sku, names.GetValueOrDefault(r.ItemId, string.Empty),
                    r.Lines, r.TotalShipped,
                    r.Lines == 0 ? 0m : Math.Round(r.TotalShipped / r.Lines, 4),
                    r.First, r.Last,
                    orderCounts.GetValueOrDefault(r.ItemId),
                    Math.Round((decimal)(r.Lines / days), 4));
            })
            .OrderByDescending(s => s.LinesPerDay)
            .ToList();
    }

    /// <summary>
    /// Reports how much of the §10 data groundwork is actually populated, so readiness can
    /// be inspected rather than assumed.
    /// </summary>
    public async Task<AlgorithmReadinessDto> GetReadinessAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(warehouseId, ct);

        var locations = await _db.Locations.Where(l => l.WarehouseId == warehouseId).ToListAsync(ct);
        var items = await _db.Items.Where(i => i.AccountId == _scope.AccountId).ToListAsync(ct);
        var orders = await _db.Orders.Where(o => o.WarehouseId == warehouseId).ToListAsync(ct);
        var balances = await _db.InventoryBalances.Where(b => b.WarehouseId == warehouseId).ToListAsync(ct);

        var checks = new List<ReadinessCheckDto>
        {
            // --- Doc §10 "Location uzerinde" ---
            Check("Location", "X/Y coordinates", "§10", locations.Count,
                locations.Count(l => l.CoordinateX is not null && l.CoordinateY is not null)),
            Check("Location", "Pick sequence", "§10", locations.Count,
                locations.Count(l => l.PickSequence is not null)),
            Check("Location", "Putaway sequence", "§10", locations.Count,
                locations.Count(l => l.PutawaySequence is not null)),
            Check("Location", "Capacity (weight or volume)", "§10", locations.Count,
                locations.Count(l => l.MaxWeight is not null || l.MaxVolume is not null
                                     || l.LocationProfileId is not null)),
            Check("Location", "Type and profile", "§10", locations.Count,
                locations.Count(l => l.LocationProfileId is not null)),
            Check("Location", "Distance to receiving/packing/shipping", "§10", locations.Count,
                locations.Count(l => l.DistanceToReceiving is not null
                                     || l.DistanceToPacking is not null
                                     || l.DistanceToShipping is not null)),
            Check("Location", "Accessibility score", "§10", locations.Count,
                locations.Count(l => l.AccessibilityScore is not null)),
            Check("Location", "Max concurrent workers", "§10", locations.Count,
                locations.Count(l => l.MaxConcurrentWorkers is not null)),

            // --- Doc §10 "Item uzerinde" ---
            Check("Item", "Dimensions and weight", "§10", items.Count,
                items.Count(i => i.Weight is not null || i.Volume is not null)),
            Check("Item", "Volume", "§10", items.Count, items.Count(i => i.Volume is not null)),
            Check("Item", "Fragility flag", "§10", items.Count, items.Count),
            Check("Item", "Hazardous-material flag", "§10", items.Count, items.Count),
            Check("Item", "Temperature requirements", "§10", items.Count,
                items.Count(i => !i.IsTemperatureControlled
                                 || i.MinimumStorageTemperature is not null
                                 || i.MaximumStorageTemperature is not null)),
            Check("Item", "Lot / serial / expiration tracking", "§10", items.Count, items.Count),
            Check("Item", "Season and other attributes", "§10", items.Count,
                await _db.ItemAttributeValues.Select(v => v.ItemId).Distinct().CountAsync(ct)),
            Check("Item", "Storage constraints (default zones)", "§10", items.Count,
                items.Count(i => i.DefaultPutawayZoneId is not null || i.DefaultPickZoneId is not null)),

            // --- Doc §10 "Order uzerinde" ---
            Check("Order", "Order date", "§10", orders.Count, orders.Count),
            Check("Order", "Required ship date", "§10", orders.Count,
                orders.Count(o => o.RequiredShipDate is not null)),
            Check("Order", "Priority", "§10", orders.Count, orders.Count),
            Check("Order", "Carrier and service level", "§10", orders.Count,
                orders.Count(o => o.Carrier is not null || o.ServiceLevel is not null)),
            Check("Order", "Order type", "§10", orders.Count, orders.Count),
            Check("Order", "Totals (lines, units, weight, volume)", "§10", orders.Count,
                orders.Count(o => o.TotalLineCount > 0)),

            // --- Doc §10 "Inventory uzerinde" ---
            Check("Inventory", "Receipt date into the warehouse", "§10", balances.Count, balances.Count),
            Check("Inventory", "Lot manufacture and expiration dates", "§10",
                await _db.Lots.CountAsync(ct),
                await _db.Lots.CountAsync(l => l.ExpirationDate != null, ct)),
            Check("Inventory", "Last movement date", "§10", balances.Count,
                balances.Count(b => b.LastMovementAt is not null)),
            Check("Inventory", "Inventory status", "§10", balances.Count, balances.Count),
            Check("Inventory", "License plate information", "§10", balances.Count,
                balances.Count(b => b.LicensePlateId is not null)),
            Check("Inventory", "Location information", "§10", balances.Count, balances.Count),

            // --- Faz 6 tables ---
            Check("Faz 6", "Historical order data", "Faz 6",
                await _db.OrderHistory.CountAsync(h => h.WarehouseId == warehouseId, ct),
                await _db.OrderHistory.CountAsync(h => h.WarehouseId == warehouseId, ct)),
            Check("Faz 6", "Algorithm configuration table", "Faz 6",
                await _db.AlgorithmConfigurations.CountAsync(c => c.AccountId == _scope.AccountId, ct),
                await _db.AlgorithmConfigurations.CountAsync(c => c.AccountId == _scope.AccountId, ct)),
            Check("Faz 6", "Slotting recommendation records", "Faz 6",
                await _db.SlottingRecommendations.CountAsync(r => r.WarehouseId == warehouseId, ct),
                await _db.SlottingRecommendations.CountAsync(r => r.WarehouseId == warehouseId, ct)),
            Check("Faz 6", "Picking plan and route records", "Faz 6",
                await _db.PickingPlans.CountAsync(p => p.WarehouseId == warehouseId, ct),
                await _db.PickingPlans.CountAsync(p => p.WarehouseId == warehouseId, ct))
        };

        return new AlgorithmReadinessDto(
            warehouseId, checks, checks.Count, checks.Count(c => c.IsReady));
    }

    private static ReadinessCheckDto Check(
        string category, string dataPoint, string reference, int total, int populated)
        => new(category, dataPoint, reference, total, populated, total > 0 && populated > 0);

    private static readonly System.Linq.Expressions.Expression<
        Func<SlottingRecommendation, SlottingRecommendationDto>> RecommendationProjection =
        r => new SlottingRecommendationDto(
            r.Id, r.WarehouseId, r.ItemId, r.Item.Sku,
            r.CurrentLocationId, r.CurrentLocation != null ? r.CurrentLocation.Code : null,
            r.RecommendedLocationId, r.RecommendedLocation.Code,
            r.Score, r.RecommendationReason, r.AlgorithmName, r.AlgorithmVersion,
            r.GeneratedAt, r.WasAccepted, r.DecidedAt, r.DecidedBy);
}
