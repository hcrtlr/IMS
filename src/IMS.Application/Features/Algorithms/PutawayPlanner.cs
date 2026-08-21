using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.Algorithms;

/// <summary>
/// Answers the putaway question directly: for this item and this quantity, which locations
/// should it go to, and how many units in each.
///
/// The decision is made in two separate stages, deliberately kept apart:
///
///   1. <b>Eligibility</b> - hard rules. A location either may legally hold the item or it
///      may not: hazardous goods, storage temperature, category restrictions and mixing
///      rules. These mirror the rules receiving and putaway already enforce, so a plan can
///      never propose a location that the putaway itself would reject.
///
///   2. <b>Ranking</b> - preferences among the locations that survived stage 1. These are
///      judgement calls, weighted below and open to tuning; none of them can override a
///      stage 1 rule.
///
/// Capacity is computed from what the location already holds, so the plan reports how many
/// units actually fit rather than assuming an empty shelf.
/// </summary>
public class PutawayPlanner
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;

    public PutawayPlanner(IApplicationDbContext db, ScopeGuard scope)
    {
        _db = db;
        _scope = scope;
    }

    // --- Ranking weights ----------------------------------------------------
    // Deliberately named rather than inlined: these are the tunable part of the algorithm,
    // and the AlgorithmConfigurations table exists to move them out of code later.

    /// <summary>Putting a SKU where some of it already sits keeps picks in one place.</summary>
    private const double ConsolidationBonus = 100d;

    /// <summary>How strongly a fast-moving item is pulled towards the packing area.</summary>
    private const double ProximityWeight = 12d;

    /// <summary>A pick face earns its keep only for items that actually move.</summary>
    private const double PickFaceBonus = 35d;

    /// <summary>Slow movers belong in reserve, out of the way of the pick path.</summary>
    private const double ReserveBonus = 25d;

    /// <summary>Easy-to-reach locations win ties.</summary>
    private const double AccessibilityWeight = 0.4d;

    /// <summary>Lines shipped per day at or above this counts as a fast mover.</summary>
    private const decimal FastMoverLinesPerDay = 0.5m;

    public async Task<PutawayPlanDto> PlanAsync(
        Guid warehouseId, Guid itemId, decimal quantity, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(warehouseId, ct);

        if (quantity <= 0)
            throw new BusinessRuleViolationException("Quantity must be greater than zero.");

        var item = await _db.Items
            .FirstOrDefaultAsync(i => i.Id == itemId && i.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(ItemMaster), itemId);

        var notes = new List<string>();

        var candidates = await LoadEligibleLocationsAsync(warehouseId, item, ct);
        if (candidates.Count == 0)
        {
            notes.Add(
                "No location in this warehouse may hold this item. Check that a putaway-enabled " +
                "location exists whose zone, temperature band and category rules accept it.");

            return Empty(item, quantity, notes);
        }

        var load = await LoadCurrentLoadAsync(warehouseId, itemId, ct);
        var linesPerDay = await GetVelocityAsync(warehouseId, itemId, ct);
        var isFastMover = linesPerDay >= FastMoverLinesPerDay;

        if (item.Weight is null or <= 0 && item.Volume is null or <= 0)
        {
            notes.Add(
                "This item has neither a weight nor a volume, so how much of it fits in a " +
                "location cannot be worked out. Fill those in on the item to get real capacity " +
                "figures instead of an unlimited one.");
        }

        var scored = candidates
            .Select(location => Evaluate(location, item, load, isFastMover))
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Location.PutawaySequence ?? int.MaxValue)
            .ThenBy(c => c.Location.Code)
            .ToList();

        // Walk the ranked list, filling each location up to what it can actually take.
        var lines = new List<PutawayPlanLineDto>();
        var remaining = quantity;

        foreach (var candidate in scored)
        {
            if (remaining <= 0) break;

            var take = candidate.UnitsThatFit is null
                ? remaining
                : Math.Min(remaining, candidate.UnitsThatFit.Value);

            if (take <= 0) continue;

            lines.Add(candidate.ToLine(take, isFastMover));
            remaining -= take;
        }

        if (remaining > 0)
        {
            notes.Add(
                $"{remaining} unit(s) could not be placed: every eligible location is full. " +
                "Free up space, raise a location's capacity, or add a location that accepts " +
                "this item.");
        }

        return new PutawayPlanDto(
            item.Id, item.Sku, item.Name,
            quantity, quantity - remaining, remaining,
            item.Weight, item.Volume,
            linesPerDay, isFastMover,
            lines, notes);
    }

    // --- Stage 1: eligibility -----------------------------------------------

    /// <summary>
    /// The same hard rules receiving and putaway apply, so a plan cannot propose a location
    /// the putaway would then refuse.
    /// </summary>
    private async Task<List<Location>> LoadEligibleLocationsAsync(
        Guid warehouseId, ItemMaster item, CancellationToken ct)
    {
        var q = _db.Locations
            .Include(l => l.Zone)
            .Include(l => l.LocationProfile)
            .Where(l => l.WarehouseId == warehouseId && l.IsActive && l.IsPutawayAllowed);

        if (item.IsHazardous)
        {
            q = q.Where(l => l.LocationType == LocationType.DangerousGoods
                             || l.Zone.ZoneType == ZoneType.HazardousMaterial);
        }

        if (item.IsTemperatureControlled)
        {
            // The location's band must sit inside what the item tolerates - a freezer is not
            // an acceptable home for something that must stay between 2 and 6 degrees.
            q = q.Where(l => l.LocationProfile != null
                             && (l.LocationProfile.TemperatureMin != null
                                 || l.LocationProfile.TemperatureMax != null));

            if (item.MinimumStorageTemperature is { } min)
                q = q.Where(l => l.LocationProfile!.TemperatureMin == null
                                 || l.LocationProfile.TemperatureMin >= min);

            if (item.MaximumStorageTemperature is { } max)
                q = q.Where(l => l.LocationProfile!.TemperatureMax == null
                                 || l.LocationProfile.TemperatureMax <= max);
        }

        if (item.CategoryId is { } categoryId)
        {
            q = q.Where(l => l.LocationProfile == null
                             || l.LocationProfile.AllowedItemCategoryId == null
                             || l.LocationProfile.AllowedItemCategoryId == categoryId);
        }

        // A profile that forbids mixing must not already hold something else.
        var itemId = item.Id;
        q = q.Where(l => l.LocationProfile == null
                         || l.LocationProfile.IsMixedItemAllowed
                         || !_db.InventoryBalances.Any(b =>
                             b.LocationId == l.Id && b.OnHandQuantity > 0 && b.ItemId != itemId));

        return await q.ToListAsync(ct);
    }

    // --- Capacity ------------------------------------------------------------

    private sealed record LocationLoad(decimal Weight, decimal Volume, decimal ItemQuantity);

    /// <summary>
    /// What each location is already carrying, in weight and volume, plus how much of the
    /// planned item it holds. Computed in one pass rather than per candidate.
    /// </summary>
    private async Task<Dictionary<Guid, LocationLoad>> LoadCurrentLoadAsync(
        Guid warehouseId, Guid itemId, CancellationToken ct)
    {
        var rows = await _db.InventoryBalances
            .Where(b => b.WarehouseId == warehouseId && b.OnHandQuantity > 0)
            .Select(b => new
            {
                b.LocationId,
                b.ItemId,
                b.OnHandQuantity,
                b.Item.Weight,
                b.Item.Volume
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.LocationId)
            .ToDictionary(
                g => g.Key,
                g => new LocationLoad(
                    g.Sum(r => r.OnHandQuantity * (r.Weight ?? 0m)),
                    g.Sum(r => r.OnHandQuantity * (r.Volume ?? 0m)),
                    g.Where(r => r.ItemId == itemId).Sum(r => r.OnHandQuantity)));
    }

    private async Task<decimal> GetVelocityAsync(Guid warehouseId, Guid itemId, CancellationToken ct)
    {
        var history = await _db.OrderHistory
            .Where(h => h.AccountId == _scope.AccountId
                        && h.WarehouseId == warehouseId
                        && h.ItemId == itemId)
            .Select(h => h.ShippedAt)
            .ToListAsync(ct);

        if (history.Count == 0) return 0m;

        var days = Math.Max(1d, (history.Max() - history.Min()).TotalDays);
        return Math.Round((decimal)(history.Count / days), 4);
    }

    // --- Stage 2: ranking ----------------------------------------------------

    private sealed record Candidate(
        Location Location,
        decimal? UnitsThatFit,
        decimal? RemainingWeight,
        decimal? RemainingVolume,
        bool AlreadyHoldsItem,
        double Score,
        string Reason)
    {
        public PutawayPlanLineDto ToLine(decimal quantity, bool isFastMover) => new(
            Location.Id, Location.Code, Location.Zone.Code, Location.Zone.ZoneType,
            Location.LocationType, quantity, UnitsThatFit,
            RemainingWeight, RemainingVolume,
            Location.DistanceToPacking, Location.AccessibilityScore,
            AlreadyHoldsItem, Math.Round(Score, 2), Reason);
    }

    private Candidate Evaluate(
        Location location, ItemMaster item,
        IReadOnlyDictionary<Guid, LocationLoad> load, bool isFastMover)
    {
        var held = load.GetValueOrDefault(location.Id, new LocationLoad(0m, 0m, 0m));

        var maxWeight = location.MaxWeight ?? location.LocationProfile?.MaxWeight;
        var maxVolume = location.MaxVolume ?? location.LocationProfile?.MaxVolume;

        var remainingWeight = maxWeight is null ? (decimal?)null : Math.Max(0m, maxWeight.Value - held.Weight);
        var remainingVolume = maxVolume is null ? (decimal?)null : Math.Max(0m, maxVolume.Value - held.Volume);

        var unitsThatFit = UnitsThatFit(item, remainingWeight, remainingVolume);

        var alreadyHoldsItem = held.ItemQuantity > 0m;
        var reasons = new List<string>();
        var score = 0d;

        if (alreadyHoldsItem)
        {
            score += ConsolidationBonus;
            reasons.Add("already holds this item");
        }

        if (isFastMover)
        {
            if (location.DistanceToPacking is { } distance)
            {
                score -= (double)distance * ProximityWeight / 100d;
                reasons.Add($"{distance} from packing");
            }

            if (location.LocationType == LocationType.PickFace
                || location.Zone.ZoneType == ZoneType.Picking)
            {
                score += PickFaceBonus;
                reasons.Add("pick face for a fast mover");
            }
        }
        else if (location.LocationType == LocationType.ReserveStorage
                 || location.Zone.ZoneType == ZoneType.ReserveStorage)
        {
            score += ReserveBonus;
            reasons.Add("reserve storage suits a slow mover");
        }

        if (location.AccessibilityScore is { } accessibility)
        {
            score += accessibility * AccessibilityWeight;
            reasons.Add($"accessibility {accessibility}");
        }

        if (reasons.Count == 0) reasons.Add("eligible, no preference either way");

        return new Candidate(
            location, unitsThatFit, remainingWeight, remainingVolume,
            alreadyHoldsItem, score, string.Join("; ", reasons));
    }

    /// <summary>
    /// How many whole units fit, taking whichever of weight and volume runs out first.
    /// Null means no capacity was declared, so the location is treated as unconstrained -
    /// the plan says as much rather than inventing a number.
    /// </summary>
    private static decimal? UnitsThatFit(
        ItemMaster item, decimal? remainingWeight, decimal? remainingVolume)
    {
        decimal? byWeight = item.Weight is > 0m && remainingWeight is not null
            ? Math.Floor(remainingWeight.Value / item.Weight.Value)
            : null;

        decimal? byVolume = item.Volume is > 0m && remainingVolume is not null
            ? Math.Floor(remainingVolume.Value / item.Volume.Value)
            : null;

        if (byWeight is null) return byVolume;
        if (byVolume is null) return byWeight;

        return Math.Min(byWeight.Value, byVolume.Value);
    }

    private static PutawayPlanDto Empty(ItemMaster item, decimal quantity, List<string> notes) =>
        new(item.Id, item.Sku, item.Name, quantity, 0m, quantity,
            item.Weight, item.Volume, 0m, false,
            Array.Empty<PutawayPlanLineDto>(), notes);
}
