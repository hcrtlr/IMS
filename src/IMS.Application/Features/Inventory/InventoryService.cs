using System.Linq.Expressions;
using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IMS.Application.Features.Inventory;

/// <summary>
/// Doc §5 and §8 - inventory queries, manual stock entry, movement between locations,
/// status changes and holds. Every mutating path runs inside one database transaction
/// and writes through <see cref="InventoryLedger"/>, so rules §11.9 and §11.12 hold.
/// </summary>
public class InventoryService
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryRepository _inventory;
    private readonly InventoryLedger _ledger;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(
        IApplicationDbContext db,
        IInventoryRepository inventory,
        InventoryLedger ledger,
        ScopeGuard scope,
        IDateTimeProvider clock,
        ILogger<InventoryService> logger)
    {
        _db = db;
        _inventory = inventory;
        _ledger = ledger;
        _scope = scope;
        _clock = clock;
        _logger = logger;
    }

    // ---------------------------------------------------------------- queries

    private IQueryable<InventoryBalance> ScopedBalances()
        => _db.InventoryBalances.Where(b => b.Warehouse.AccountId == _scope.AccountId);

    /// <summary>GET /api/inventory (§12).</summary>
    public async Task<PagedResult<InventoryBalanceDto>> ListAsync(
        InventoryQuery query, CancellationToken ct = default)
    {
        var q = ScopedBalances();

        if (query.WarehouseId.HasValue) q = q.Where(b => b.WarehouseId == query.WarehouseId.Value);
        if (query.LocationId.HasValue) q = q.Where(b => b.LocationId == query.LocationId.Value);
        if (query.ItemId.HasValue) q = q.Where(b => b.ItemId == query.ItemId.Value);
        if (query.InventoryStatusId.HasValue) q = q.Where(b => b.InventoryStatusId == query.InventoryStatusId.Value);
        if (query.LotId.HasValue) q = q.Where(b => b.LotId == query.LotId.Value);
        if (query.LicensePlateId.HasValue) q = q.Where(b => b.LicensePlateId == query.LicensePlateId.Value);
        if (query.NonZeroOnly) q = q.Where(b => b.OnHandQuantity != 0m);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(b => b.Item.Sku.ToLower().Contains(term)
                             || b.Item.Name.ToLower().Contains(term)
                             || b.Location.Code.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(b => b.Item.Sku).ThenBy(b => b.Location.Code)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);

        return new PagedResult<InventoryBalanceDto>(items, total, query.Page, query.PageSize);
    }

    /// <summary>GET /api/inventory/by-item/{itemId} (§12).</summary>
    public async Task<IReadOnlyList<InventoryBalanceDto>> ByItemAsync(
        Guid itemId, Guid? warehouseId, CancellationToken ct = default)
    {
        await _scope.EnsureItemAsync(itemId, ct);

        var q = ScopedBalances().Where(b => b.ItemId == itemId && b.OnHandQuantity != 0m);
        if (warehouseId.HasValue) q = q.Where(b => b.WarehouseId == warehouseId.Value);

        return await q
            // FEFO-friendly ordering so the view matches how stock would be consumed.
            .OrderBy(b => b.Lot!.ExpirationDate ?? DateTimeOffset.MaxValue)
            .ThenBy(b => b.ReceivedAt)
            .Select(ToDto)
            .ToListAsync(ct);
    }

    /// <summary>GET /api/inventory/by-location/{locationId} (§12).</summary>
    public async Task<IReadOnlyList<InventoryBalanceDto>> ByLocationAsync(
        Guid locationId, CancellationToken ct = default)
        => await ScopedBalances()
            .Where(b => b.LocationId == locationId && b.OnHandQuantity != 0m)
            .OrderBy(b => b.Item.Sku)
            .Select(ToDto)
            .ToListAsync(ct);

    /// <summary>Warehouse-wide stock position per item.</summary>
    public async Task<IReadOnlyList<ItemStockSummaryDto>> SummaryAsync(
        Guid warehouseId, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(warehouseId, ct);

        var q = ScopedBalances().Where(b => b.WarehouseId == warehouseId && b.OnHandQuantity != 0m);

        // Split into three translatable queries rather than one. EF Core cannot translate
        // Distinct().Count() inside a GroupBy projection, and materialising every balance
        // row to group in memory would not scale for a real warehouse.
        var totals = await q
            .GroupBy(b => new { b.ItemId, b.Item.Sku, b.Item.Name, BaseUom = b.Item.BaseUom.Code })
            .Select(g => new
            {
                g.Key.ItemId, g.Key.Sku, g.Key.Name, g.Key.BaseUom,
                OnHand = g.Sum(b => b.OnHandQuantity),
                Allocated = g.Sum(b => b.AllocatedQuantity),
                Hold = g.Sum(b => b.HoldQuantity),
                Available = g.Sum(b => b.AvailableQuantity)
            })
            .ToListAsync(ct);

        var locationCounts = await q
            .Select(b => new { b.ItemId, b.LocationId })
            .Distinct()
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        var lotCounts = await q
            .Where(b => b.LotId != null)
            .Select(b => new { b.ItemId, b.LotId })
            .Distinct()
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ItemId, x => x.Count, ct);

        return totals
            .OrderBy(t => t.Sku)
            .Select(t => new ItemStockSummaryDto(
                t.ItemId, t.Sku, t.Name, t.BaseUom,
                t.OnHand, t.Allocated, t.Hold, t.Available,
                locationCounts.GetValueOrDefault(t.ItemId),
                lotCounts.GetValueOrDefault(t.ItemId)))
            .ToList();
    }

    /// <summary>GET /api/inventory/transactions (§12) - the immutable ledger (§5.6).</summary>
    public async Task<PagedResult<InventoryTransactionDto>> TransactionsAsync(
        TransactionQuery query, CancellationToken ct = default)
    {
        var q = _db.InventoryTransactions
            .Where(t => t.Warehouse.AccountId == _scope.AccountId);

        if (query.WarehouseId.HasValue) q = q.Where(t => t.WarehouseId == query.WarehouseId.Value);
        if (query.ItemId.HasValue) q = q.Where(t => t.ItemId == query.ItemId.Value);
        if (query.LotId.HasValue) q = q.Where(t => t.LotId == query.LotId.Value);
        if (query.TransactionType.HasValue) q = q.Where(t => t.TransactionType == query.TransactionType.Value);
        if (query.ReferenceType.HasValue) q = q.Where(t => t.ReferenceType == query.ReferenceType.Value);
        if (query.ReferenceId.HasValue) q = q.Where(t => t.ReferenceId == query.ReferenceId.Value);
        if (query.CorrelationId.HasValue) q = q.Where(t => t.CorrelationId == query.CorrelationId.Value);
        if (query.FromDate.HasValue) q = q.Where(t => t.CreatedAt >= query.FromDate.Value);
        if (query.ToDate.HasValue) q = q.Where(t => t.CreatedAt <= query.ToDate.Value);

        if (query.LocationId.HasValue)
        {
            var loc = query.LocationId.Value;
            q = q.Where(t => t.FromLocationId == loc || t.ToLocationId == loc);
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(t => new InventoryTransactionDto(
                t.Id, t.WarehouseId,
                t.ItemId, t.Item.Sku,
                t.FromLocationId, t.FromLocation != null ? t.FromLocation.Code : null,
                t.ToLocationId, t.ToLocation != null ? t.ToLocation.Code : null,
                t.FromInventoryStatus != null ? t.FromInventoryStatus.Code : null,
                t.ToInventoryStatus != null ? t.ToInventoryStatus.Code : null,
                t.LotId, t.Lot != null ? t.Lot.LotNumber : null,
                t.SerialId, t.Serial != null ? t.Serial.Serial : null,
                t.LicensePlateId, t.LicensePlate != null ? t.LicensePlate.Code : null,
                t.TransactionType, t.Quantity,
                t.ReferenceType, t.ReferenceId,
                t.CorrelationId, t.PerformedBy, t.CreatedAt, t.Notes))
            .ToListAsync(ct);

        return new PagedResult<InventoryTransactionDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<IReadOnlyList<InventoryStatusDto>> ListStatusesAsync(CancellationToken ct = default)
        => await _db.InventoryStatuses
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new InventoryStatusDto(
                s.Id, s.Code, s.Name, s.Description,
                s.IsAllocatable, s.IsPhysicalStock, s.DisplayOrder, s.IsActive))
            .ToListAsync(ct);

    // ---------------------------------------------------------------- mutations

    /// <summary>
    /// Faz 2 "Manuel stok girisi" - books stock straight into a location, creating the
    /// lot or serial record on the fly when the item is tracked that way.
    /// </summary>
    public async Task<InventoryBalanceDto> ManualEntryAsync(
        ManualStockEntryRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);
        await _scope.EnsureLocationAsync(request.LocationId, request.WarehouseId, ct);
        await _scope.EnsureItemAsync(request.ItemId, ct);

        if (request.Quantity <= 0)
            throw new BusinessRuleViolationException("Quantity must be greater than zero.");

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var item = await _db.Items.FirstAsync(i => i.Id == request.ItemId, token);

            var baseQuantity = await ConvertToBaseAsync(request.ItemId, request.UomId, request.Quantity, token);

            var statusId = request.InventoryStatusId ?? await DefaultStatusIdAsync(token);

            var lotId = await ResolveLotAsync(item, request.LotNumber, request.ManufactureDate,
                request.ExpirationDate, request.SupplierLotNumber, token);

            var serialId = await ResolveSerialAsync(item, request.SerialNumber, lotId, token);

            var correlationId = Guid.NewGuid();

            var balance = await _ledger.AddAsync(new StockMutation
            {
                WarehouseId = request.WarehouseId,
                ItemId = request.ItemId,
                Quantity = baseQuantity,
                TransactionType = InventoryTransactionType.Receipt,
                ReferenceType = TransactionReferenceType.Manual,
                CorrelationId = correlationId,
                ToLocationId = request.LocationId,
                ToInventoryStatusId = statusId,
                LotId = lotId,
                SerialId = serialId,
                LicensePlateId = request.LicensePlateId,
                Notes = request.Notes ?? "Manual stock entry"
            }, token);

            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Manual stock entry: {Quantity} of item {ItemId} into location {LocationId} (correlation {CorrelationId})",
                baseQuantity, request.ItemId, request.LocationId, correlationId);

            return await GetBalanceDtoAsync(balance.Id, token);
        }, ct);
    }

    /// <summary>
    /// Doc §8 - POST /api/inventory/movements. Decrement source, increment destination
    /// and write the ledger row, all in one database transaction (rule §11.12).
    /// </summary>
    public async Task<InventoryBalanceDto> MoveAsync(
        InventoryMovementRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);
        await _scope.EnsureLocationAsync(request.FromLocationId, request.WarehouseId, ct);
        await _scope.EnsureLocationAsync(request.ToLocationId, request.WarehouseId, ct);

        if (request.Quantity <= 0)
            throw new BusinessRuleViolationException("Quantity must be greater than zero.");

        await EnsureLocationAcceptsItemAsync(request.ToLocationId, request.ItemId, ct);

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var (_, to) = await _ledger.MoveAsync(new StockMutation
            {
                WarehouseId = request.WarehouseId,
                ItemId = request.ItemId,
                Quantity = request.Quantity,
                TransactionType = InventoryTransactionType.Movement,
                ReferenceType = TransactionReferenceType.Movement,
                CorrelationId = Guid.NewGuid(),
                FromLocationId = request.FromLocationId,
                ToLocationId = request.ToLocationId,
                FromInventoryStatusId = request.InventoryStatusId,
                ToInventoryStatusId = request.InventoryStatusId,
                LotId = request.LotId,
                SerialId = request.SerialId,
                LicensePlateId = request.LicensePlateId,
                Notes = request.Notes
            }, token);

            await _db.SaveChangesAsync(token);

            return await GetBalanceDtoAsync(to.Id, token);
        }, ct);
    }

    /// <summary>
    /// Doc §12 - POST /api/inventory/status-change. Same location, different status:
    /// e.g. moving Available stock into QualityHold after an inspection finding.
    /// </summary>
    public async Task<InventoryBalanceDto> ChangeStatusAsync(
        StatusChangeRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);
        await _scope.EnsureLocationAsync(request.LocationId, request.WarehouseId, ct);

        if (request.Quantity <= 0)
            throw new BusinessRuleViolationException("Quantity must be greater than zero.");

        if (request.FromInventoryStatusId == request.ToInventoryStatusId)
            throw new BusinessRuleViolationException("Source and target status are the same.");

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var (_, to) = await _ledger.MoveAsync(new StockMutation
            {
                WarehouseId = request.WarehouseId,
                ItemId = request.ItemId,
                Quantity = request.Quantity,
                TransactionType = InventoryTransactionType.StatusChange,
                ReferenceType = TransactionReferenceType.Manual,
                CorrelationId = Guid.NewGuid(),
                FromLocationId = request.LocationId,
                ToLocationId = request.LocationId,
                FromInventoryStatusId = request.FromInventoryStatusId,
                ToInventoryStatusId = request.ToInventoryStatusId,
                LotId = request.LotId,
                SerialId = request.SerialId,
                LicensePlateId = request.LicensePlateId,
                Notes = request.Notes
            }, token);

            await _db.SaveChangesAsync(token);

            return await GetBalanceDtoAsync(to.Id, token);
        }, ct);
    }

    /// <summary>
    /// Faz 5 "hold ve damaged islemleri" - blocks quantity from allocation without
    /// changing its status. HoldQuantity feeds the §5.1 availability formula directly.
    /// </summary>
    public async Task<InventoryBalanceDto> PlaceHoldAsync(HoldRequest request, CancellationToken ct = default)
        => await AdjustHoldAsync(request, place: true, ct);

    public async Task<InventoryBalanceDto> ReleaseHoldAsync(HoldRequest request, CancellationToken ct = default)
        => await AdjustHoldAsync(request, place: false, ct);

    private async Task<InventoryBalanceDto> AdjustHoldAsync(
        HoldRequest request, bool place, CancellationToken ct)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var balance = await _inventory.GetBalanceForUpdateAsync(request.InventoryBalanceId, token)
                ?? throw new NotFoundException(nameof(InventoryBalance), request.InventoryBalanceId);

            await EnsureBalanceInScopeAsync(balance, token);

            var sku = await _db.Items.Where(i => i.Id == balance.ItemId)
                .Select(i => i.Sku).FirstAsync(token);

            if (place) balance.PlaceHold(request.Quantity, sku);
            else balance.ReleaseHold(request.Quantity);

            // A hold moves no stock, so it is recorded as a StatusChange-type ledger entry
            // against the same location, keeping rule §11.9 satisfied.
            _ledger.RecordNonPhysical(new StockMutation
            {
                WarehouseId = balance.WarehouseId,
                ItemId = balance.ItemId,
                Quantity = request.Quantity,
                TransactionType = InventoryTransactionType.StatusChange,
                ReferenceType = TransactionReferenceType.Manual,
                CorrelationId = Guid.NewGuid(),
                FromLocationId = balance.LocationId,
                ToLocationId = balance.LocationId,
                FromInventoryStatusId = balance.InventoryStatusId,
                ToInventoryStatusId = balance.InventoryStatusId,
                LotId = balance.LotId,
                SerialId = balance.SerialId,
                LicensePlateId = balance.LicensePlateId,
                Notes = request.Notes ?? (place ? "Hold placed" : "Hold released")
            });

            await _db.SaveChangesAsync(token);

            return await GetBalanceDtoAsync(balance.Id, token);
        }, ct);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Rule §11.8 - a destination must be compatible with the item's temperature and
    /// hazardous-material requirements.
    /// </summary>
    private async Task EnsureLocationAcceptsItemAsync(Guid locationId, Guid itemId, CancellationToken ct)
    {
        var item = await _db.Items
            .Where(i => i.Id == itemId)
            .Select(i => new
            {
                i.Sku, i.IsHazardous, i.IsTemperatureControlled,
                i.MinimumStorageTemperature, i.MaximumStorageTemperature
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Item", itemId);

        if (!item.IsHazardous && !item.IsTemperatureControlled) return;

        var location = await _db.Locations
            .Where(l => l.Id == locationId)
            .Select(l => new
            {
                l.Code, l.LocationType,
                ZoneType = l.Zone.ZoneType,
                ProfileMin = l.LocationProfile != null ? l.LocationProfile.TemperatureMin : null,
                ProfileMax = l.LocationProfile != null ? l.LocationProfile.TemperatureMax : null,
                HasProfile = l.LocationProfileId != null
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Location", locationId);

        if (item.IsHazardous
            && location.LocationType != LocationType.DangerousGoods
            && location.ZoneType != ZoneType.HazardousMaterial)
        {
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is hazardous and cannot be stored in location '{location.Code}'.",
                ruleNumber: 8);
        }

        if (item.IsTemperatureControlled)
        {
            var profile = new Domain.Entities.MasterData.LocationProfile
            {
                TemperatureMin = location.ProfileMin,
                TemperatureMax = location.ProfileMax
            };

            if (!location.HasProfile ||
                !profile.SupportsTemperatureRange(
                    item.MinimumStorageTemperature, item.MaximumStorageTemperature))
            {
                throw new BusinessRuleViolationException(
                    $"Location '{location.Code}' does not satisfy the storage temperature " +
                    $"requirements of item '{item.Sku}'.",
                    ruleNumber: 8);
            }
        }
    }

    private async Task EnsureBalanceInScopeAsync(InventoryBalance balance, CancellationToken ct)
    {
        var ok = await _db.Warehouses
            .AnyAsync(w => w.Id == balance.WarehouseId && w.AccountId == _scope.AccountId, ct);

        if (!ok) throw new NotFoundException(nameof(InventoryBalance), balance.Id);
    }

    /// <summary>Normalises a quantity expressed in any UOM into the item's base UOM.</summary>
    public async Task<decimal> ConvertToBaseAsync(
        Guid itemId, Guid? uomId, decimal quantity, CancellationToken ct)
    {
        if (uomId is null) return quantity;

        var item = await _db.Items.Where(i => i.Id == itemId)
            .Select(i => new { i.BaseUomId, i.Sku }).FirstAsync(ct);

        if (uomId == item.BaseUomId) return quantity;

        var conversion = await _db.ItemUoms
            .Where(u => u.ItemId == itemId && u.UomId == uomId.Value)
            .Select(u => (decimal?)u.ConversionQuantity)
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' has no conversion defined for the supplied unit of measure.");

        return quantity * conversion;
    }

    private async Task<Guid> DefaultStatusIdAsync(CancellationToken ct)
        => await _db.InventoryStatuses
            .Where(s => s.Code == InventoryStatus.Available)
            .Select(s => s.Id)
            .FirstAsync(ct);

    /// <summary>
    /// Finds or creates the lot for a receipt. Rule §11.6 - an expiration-tracked item
    /// must end up with an expiration date, derived from ShelfLifeDays when the caller
    /// does not supply one.
    /// </summary>
    public async Task<Guid?> ResolveLotAsync(
        Domain.Entities.MasterData.ItemMaster item,
        string? lotNumber,
        DateTimeOffset? manufactureDate,
        DateTimeOffset? expirationDate,
        string? supplierLotNumber,
        CancellationToken ct)
    {
        if (!item.IsLotTracked)
        {
            if (!string.IsNullOrWhiteSpace(lotNumber))
                throw new BusinessRuleViolationException(
                    $"Item '{item.Sku}' is not lot tracked; a lot number must not be supplied.");

            return null;
        }

        if (string.IsNullOrWhiteSpace(lotNumber))
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is lot tracked; a lot number is required.");

        var existing = await _db.Lots
            .FirstOrDefaultAsync(l => l.ItemId == item.Id && l.LotNumber == lotNumber, ct);

        if (existing is not null)
        {
            if (item.IsExpirationTracked && existing.ExpirationDate is null)
                throw new BusinessRuleViolationException(
                    $"Lot '{lotNumber}' has no expiration date but item '{item.Sku}' is expiration tracked.",
                    ruleNumber: 6);

            return existing.Id;
        }

        var resolvedExpiry = expirationDate;

        if (item.IsExpirationTracked && resolvedExpiry is null)
        {
            if (item.ShelfLifeDays is null or <= 0)
                throw new BusinessRuleViolationException(
                    $"Item '{item.Sku}' is expiration tracked. Supply an expiration date, " +
                    $"or define ShelfLifeDays on the item so one can be derived.",
                    ruleNumber: 6);

            resolvedExpiry = (manufactureDate ?? _clock.UtcNow).AddDays(item.ShelfLifeDays.Value);
        }

        var lot = new Lot
        {
            ItemId = item.Id,
            LotNumber = lotNumber,
            ManufactureDate = manufactureDate,
            ReceivedDate = _clock.UtcNow,
            ExpirationDate = resolvedExpiry,
            SupplierLotNumber = supplierLotNumber,
            Status = LotStatus.Active
        };

        _db.Lots.Add(lot);
        await _db.SaveChangesAsync(ct);

        return lot.Id;
    }

    /// <summary>Finds or creates the serial record for a serial-tracked receipt.</summary>
    public async Task<Guid?> ResolveSerialAsync(
        Domain.Entities.MasterData.ItemMaster item,
        string? serialNumber,
        Guid? lotId,
        CancellationToken ct)
    {
        if (!item.IsSerialTracked)
        {
            if (!string.IsNullOrWhiteSpace(serialNumber))
                throw new BusinessRuleViolationException(
                    $"Item '{item.Sku}' is not serial tracked; a serial number must not be supplied.");

            return null;
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is serial tracked; a serial number is required.", ruleNumber: 5);

        var existing = await _db.SerialNumbers
            .FirstOrDefaultAsync(s => s.ItemId == item.Id && s.Serial == serialNumber, ct);

        if (existing is not null)
        {
            // A serial already in stock cannot be received again.
            var inStock = await _db.InventoryBalances
                .AnyAsync(b => b.SerialId == existing.Id && b.OnHandQuantity > 0, ct);

            if (inStock)
                throw new DuplicateEntityException(
                    $"Serial '{serialNumber}' for item '{item.Sku}' is already in stock.");

            return existing.Id;
        }

        var serial = new SerialNumber
        {
            ItemId = item.Id,
            Serial = serialNumber,
            LotId = lotId,
            Status = SerialStatus.Available
        };

        _db.SerialNumbers.Add(serial);
        await _db.SaveChangesAsync(ct);

        return serial.Id;
    }

    private async Task<InventoryBalanceDto> GetBalanceDtoAsync(Guid balanceId, CancellationToken ct)
        => await _db.InventoryBalances
            .Where(b => b.Id == balanceId)
            .Select(ToDto)
            .FirstAsync(ct);

    /// <summary>
    /// Shared projection so every balance endpoint returns the same shape.
    ///
    /// This must be an expression tree, not a method: EF Core cannot translate a method
    /// call inside Select, so it would materialise the entity with unloaded navigations
    /// and then null-reference on Location/Item/InventoryStatus.
    /// </summary>
    private static readonly Expression<Func<InventoryBalance, InventoryBalanceDto>> ToDto =
        b => new InventoryBalanceDto(
            b.Id, b.WarehouseId,
            b.LocationId, b.Location.Code, b.Location.Zone.Code, b.Location.Zone.ZoneType,
            b.ItemId, b.Item.Sku, b.Item.Name,
            b.InventoryStatusId, b.InventoryStatus.Code, b.InventoryStatus.IsAllocatable,
            b.LotId, b.Lot != null ? b.Lot.LotNumber : null,
            b.Lot != null ? b.Lot.ExpirationDate : null,
            b.SerialId, b.Serial != null ? b.Serial.Serial : null,
            b.LicensePlateId, b.LicensePlate != null ? b.LicensePlate.Code : null,
            b.OnHandQuantity, b.AllocatedQuantity, b.HoldQuantity, b.AvailableQuantity,
            b.ReceivedAt, b.LastMovementAt, b.Version);
}
