using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.Inventory;

/// <summary>
/// Doc §5.3, §5.4, §5.5 - lots, serial numbers and license plates (containers).
/// </summary>
public class TrackingService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;

    public TrackingService(IApplicationDbContext db, ScopeGuard scope, IDateTimeProvider clock)
    {
        _db = db;
        _scope = scope;
        _clock = clock;
    }

    // ---------------------------------------------------------------- lots (§5.3)

    public async Task<IReadOnlyList<LotDto>> ListLotsAsync(
        Guid? itemId, bool expiringOnly, CancellationToken ct = default)
    {
        var q = _db.Lots.Where(l => l.Item.AccountId == _scope.AccountId);

        if (itemId.HasValue) q = q.Where(l => l.ItemId == itemId.Value);
        if (expiringOnly) q = q.Where(l => l.ExpirationDate != null);

        var now = _clock.UtcNow;

        var lots = await q
            // FEFO order: soonest expiry first. Acceptance scenario 4 relies on lots of
            // the same item staying distinct and orderable by expiration.
            .OrderBy(l => l.ExpirationDate ?? DateTimeOffset.MaxValue)
            .ThenBy(l => l.LotNumber)
            .Select(l => new
            {
                Lot = l,
                Sku = l.Item.Sku,
                OnHand = _db.InventoryBalances
                    .Where(b => b.LotId == l.Id)
                    .Sum(b => (decimal?)b.OnHandQuantity) ?? 0m
            })
            .ToListAsync(ct);

        return lots.Select(x => new LotDto(
            x.Lot.Id, x.Lot.ItemId, x.Sku, x.Lot.LotNumber,
            x.Lot.ManufactureDate, x.Lot.ReceivedDate, x.Lot.ExpirationDate,
            x.Lot.SupplierLotNumber, x.Lot.Status,
            x.Lot.RemainingShelfLifeDays(now), x.OnHand)).ToList();
    }

    public async Task<LotDto> CreateLotAsync(CreateLotRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureItemAsync(request.ItemId, ct);

        var item = await _db.Items.FirstAsync(i => i.Id == request.ItemId, ct);

        if (!item.IsLotTracked)
            throw new BusinessRuleViolationException($"Item '{item.Sku}' is not lot tracked.");

        if (await _db.Lots.AnyAsync(l => l.ItemId == request.ItemId && l.LotNumber == request.LotNumber, ct))
            throw new DuplicateEntityException(
                $"Lot '{request.LotNumber}' already exists for item '{item.Sku}'.");

        var expiry = request.ExpirationDate;

        // Doc §11.6 - expiration-tracked items must end up with a date.
        if (item.IsExpirationTracked && expiry is null)
        {
            if (item.ShelfLifeDays is null or <= 0)
                throw new BusinessRuleViolationException(
                    $"Item '{item.Sku}' is expiration tracked. Supply an expiration date, " +
                    $"or define ShelfLifeDays so one can be derived.", ruleNumber: 6);

            expiry = (request.ManufactureDate ?? _clock.UtcNow).AddDays(item.ShelfLifeDays.Value);
        }

        if (!item.IsExpirationTracked && expiry is not null)
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is not expiration tracked; an expiration date must not be supplied.");

        var lot = new Lot
        {
            ItemId = request.ItemId,
            LotNumber = request.LotNumber,
            ManufactureDate = request.ManufactureDate,
            ReceivedDate = _clock.UtcNow,
            ExpirationDate = expiry,
            SupplierLotNumber = request.SupplierLotNumber,
            Status = LotStatus.Active
        };

        _db.Lots.Add(lot);
        await _db.SaveChangesAsync(ct);

        return new LotDto(lot.Id, lot.ItemId, item.Sku, lot.LotNumber,
            lot.ManufactureDate, lot.ReceivedDate, lot.ExpirationDate,
            lot.SupplierLotNumber, lot.Status,
            lot.RemainingShelfLifeDays(_clock.UtcNow), 0m);
    }

    /// <summary>Lots already past their expiration date but still holding stock.</summary>
    public async Task<IReadOnlyList<LotDto>> ExpiredLotsAsync(
        Guid? warehouseId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;

        var rows = await _db.Lots
            .Where(l => l.Item.AccountId == _scope.AccountId
                        && l.ExpirationDate != null
                        && l.ExpirationDate <= now)
            .Select(l => new
            {
                Lot = l,
                Sku = l.Item.Sku,
                OnHand = _db.InventoryBalances
                    .Where(b => b.LotId == l.Id
                                && (warehouseId == null || b.WarehouseId == warehouseId))
                    .Sum(b => (decimal?)b.OnHandQuantity) ?? 0m
            })
            .Where(x => x.OnHand > 0)
            .OrderBy(x => x.Lot.ExpirationDate)
            .ToListAsync(ct);

        return rows.Select(x => new LotDto(
            x.Lot.Id, x.Lot.ItemId, x.Sku, x.Lot.LotNumber,
            x.Lot.ManufactureDate, x.Lot.ReceivedDate, x.Lot.ExpirationDate,
            x.Lot.SupplierLotNumber, x.Lot.Status,
            x.Lot.RemainingShelfLifeDays(now), x.OnHand)).ToList();
    }

    // ---------------------------------------------------------------- serials (§5.4)

    public async Task<IReadOnlyList<SerialNumberDto>> ListSerialsAsync(
        Guid? itemId, SerialStatus? status, CancellationToken ct = default)
    {
        var q = _db.SerialNumbers.Where(s => s.Item.AccountId == _scope.AccountId);

        if (itemId.HasValue) q = q.Where(s => s.ItemId == itemId.Value);
        if (status.HasValue) q = q.Where(s => s.Status == status.Value);

        return await q
            .OrderBy(s => s.Serial)
            .Select(s => new SerialNumberDto(
                s.Id, s.ItemId, s.Item.Sku, s.Serial,
                s.LotId, s.Lot != null ? s.Lot.LotNumber : null, s.Status))
            .ToListAsync(ct);
    }

    public async Task<SerialNumberDto> CreateSerialAsync(
        CreateSerialRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureItemAsync(request.ItemId, ct);

        var item = await _db.Items.FirstAsync(i => i.Id == request.ItemId, ct);

        if (!item.IsSerialTracked)
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is not serial tracked.", ruleNumber: 5);

        if (await _db.SerialNumbers.AnyAsync(
                s => s.ItemId == request.ItemId && s.Serial == request.Serial, ct))
            throw new DuplicateEntityException(
                $"Serial '{request.Serial}' already exists for item '{item.Sku}'.");

        var serial = new SerialNumber
        {
            ItemId = request.ItemId,
            Serial = request.Serial,
            LotId = request.LotId,
            Status = SerialStatus.Available
        };

        _db.SerialNumbers.Add(serial);
        await _db.SaveChangesAsync(ct);

        return new SerialNumberDto(serial.Id, serial.ItemId, item.Sku, serial.Serial,
            serial.LotId, null, serial.Status);
    }

    // ---------------------------------------------------------------- LPNs (§5.5)

    public async Task<IReadOnlyList<LicensePlateDto>> ListLicensePlatesAsync(
        Guid? warehouseId, Guid? locationId, CancellationToken ct = default)
    {
        var q = _db.LicensePlates.Where(lp => lp.Warehouse.AccountId == _scope.AccountId);

        if (warehouseId.HasValue) q = q.Where(lp => lp.WarehouseId == warehouseId.Value);
        if (locationId.HasValue) q = q.Where(lp => lp.CurrentLocationId == locationId.Value);

        return await q
            .OrderBy(lp => lp.Code)
            .Select(lp => new LicensePlateDto(
                lp.Id, lp.WarehouseId, lp.Code,
                lp.ParentLicensePlateId, lp.ParentLicensePlate != null ? lp.ParentLicensePlate.Code : null,
                lp.LicensePlateType,
                lp.CurrentLocationId, lp.CurrentLocation != null ? lp.CurrentLocation.Code : null,
                lp.Status,
                lp.Children.Count,
                _db.InventoryBalances.Where(b => b.LicensePlateId == lp.Id)
                    .Sum(b => (decimal?)b.OnHandQuantity) ?? 0m))
            .ToListAsync(ct);
    }

    public async Task<LicensePlateDto> CreateLicensePlateAsync(
        CreateLicensePlateRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);

        if (await _db.LicensePlates.AnyAsync(
                lp => lp.WarehouseId == request.WarehouseId && lp.Code == request.Code, ct))
            throw new DuplicateEntityException(
                $"License plate '{request.Code}' already exists in this warehouse.");

        if (request.ParentLicensePlateId.HasValue)
        {
            var parent = await _db.LicensePlates
                .FirstOrDefaultAsync(lp => lp.Id == request.ParentLicensePlateId.Value, ct)
                ?? throw new NotFoundException(nameof(LicensePlate), request.ParentLicensePlateId.Value);

            if (parent.WarehouseId != request.WarehouseId)
                throw new BusinessRuleViolationException(
                    "A license plate cannot be nested inside one from another warehouse.");
        }

        if (request.CurrentLocationId.HasValue)
            await _scope.EnsureLocationAsync(request.CurrentLocationId.Value, request.WarehouseId, ct);

        var lp = new LicensePlate
        {
            WarehouseId = request.WarehouseId,
            Code = request.Code,
            LicensePlateType = request.LicensePlateType,
            ParentLicensePlateId = request.ParentLicensePlateId,
            CurrentLocationId = request.CurrentLocationId,
            Status = LicensePlateStatus.Open
        };

        _db.LicensePlates.Add(lp);
        await _db.SaveChangesAsync(ct);

        return (await ListLicensePlatesAsync(request.WarehouseId, null, ct))
            .First(x => x.Id == lp.Id);
    }

    /// <summary>
    /// Doc §5.5 - the nested container view (Pallet -> Case 1 / Case 2 / Case 3).
    /// Loads the subtree iteratively rather than recursing per node.
    /// </summary>
    public async Task<LicensePlateTreeDto> GetLicensePlateTreeAsync(Guid id, CancellationToken ct = default)
    {
        var root = await _db.LicensePlates
            .FirstOrDefaultAsync(lp => lp.Id == id && lp.Warehouse.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(LicensePlate), id);

        // Pull the whole warehouse's plates once, then assemble in memory. Nesting is
        // shallow in practice (pallet -> case -> tote), so this beats N recursive queries.
        var all = await _db.LicensePlates
            .Where(lp => lp.WarehouseId == root.WarehouseId)
            .Select(lp => new
            {
                lp.Id, lp.Code, lp.LicensePlateType, lp.Status, lp.ParentLicensePlateId,
                LocationCode = lp.CurrentLocation != null ? lp.CurrentLocation.Code : null,
                OnHand = _db.InventoryBalances.Where(b => b.LicensePlateId == lp.Id)
                    .Sum(b => (decimal?)b.OnHandQuantity) ?? 0m
            })
            .ToListAsync(ct);

        var byParent = all
            .Where(x => x.ParentLicensePlateId != null)
            .GroupBy(x => x.ParentLicensePlateId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        LicensePlateTreeDto Build(Guid nodeId, int depth)
        {
            var node = all.First(x => x.Id == nodeId);

            // Depth guard: a cycle would otherwise recurse forever.
            var children = depth >= 10 || !byParent.TryGetValue(nodeId, out var kids)
                ? new List<LicensePlateTreeDto>()
                : kids.Select(k => Build(k.Id, depth + 1)).ToList();

            return new LicensePlateTreeDto(
                node.Id, node.Code, node.LicensePlateType, node.Status,
                node.LocationCode, node.OnHand, children);
        }

        return Build(root.Id, 0);
    }
}
