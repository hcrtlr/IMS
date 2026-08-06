using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Application.Features.Inventory;
using IMS.Domain.Entities.Outbound;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IMS.Application.Features.Outbound;

/// <summary>
/// Doc §7.3 - reserves stock for order lines.
///
/// The rules this enforces, all from doc §11:
///   §11.1 no allocation without sufficient available stock
///   §11.2 allocation never changes on-hand quantity
///   §11.4 hold, damaged and expired stock is never allocated to a normal order
///  §11.11 candidate balances are row-locked, so two orders cannot both take the same units
///
/// Acceptance scenario 3 depends on partial allocation being honest: the order must not
/// reach Allocated, stock must not go negative, and the shortfall must be reported per item.
/// </summary>
public class AllocationService
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryRepository _inventory;
    private readonly InventoryLedger _ledger;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AllocationService> _logger;

    public AllocationService(
        IApplicationDbContext db,
        IInventoryRepository inventory,
        InventoryLedger ledger,
        ScopeGuard scope,
        IDateTimeProvider clock,
        ILogger<AllocationService> logger)
    {
        _db = db;
        _inventory = inventory;
        _ledger = ledger;
        _scope = scope;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>POST /api/orders/{id}/allocate (§12).</summary>
    public async Task<AllocationResultDto> AllocateAsync(
        Guid orderId, AllocateRequest? request, CancellationToken ct = default)
    {
        var allowPartial = request?.AllowPartial ?? true;

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var order = await _db.Orders
                .Include(o => o.Details).ThenInclude(d => d.Item)
                .Include(o => o.Details).ThenInclude(d => d.Allocations)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(OrderMaster), orderId);

            if (order.Status is not (OrderStatus.Released
                or OrderStatus.PartiallyAllocated or OrderStatus.Allocated))
            {
                throw new BusinessRuleViolationException(
                    $"An order must be Released before it can be allocated (current status: {order.Status}).");
            }

            var lines = order.Details
                .Where(d => d.Status != OrderDetailStatus.Cancelled)
                .Where(d => request?.OrderDetailIds is null
                            || request.OrderDetailIds.Contains(d.Id))
                .OrderBy(d => d.LineNumber)
                .ToList();

            if (lines.Count == 0)
                throw new BusinessRuleViolationException("No allocatable lines were selected.");

            var created = new List<InventoryAllocation>();
            var shortfalls = new List<ShortfallDto>();

            foreach (var line in lines)
            {
                var remaining = line.UnallocatedQuantity;
                if (remaining <= 0) continue;

                // Row-locked, FEFO-then-FIFO ordered, allocatable statuses only (rule §11.4).
                var candidates = await _inventory.GetAllocationCandidatesForUpdateAsync(
                    order.WarehouseId,
                    line.ItemId,
                    line.RequiredLotNumber,
                    line.RequiredSerialNumber,
                    line.MinimumShelfLifeDays,
                    _clock.UtcNow,
                    token);

                foreach (var balance in candidates)
                {
                    if (remaining <= 0) break;

                    var available = balance.ComputeAvailable();
                    if (available <= 0) continue;

                    var take = Math.Min(available, remaining);

                    // Rule §11.1 and §11.2: raises AllocatedQuantity only, never OnHand.
                    balance.Allocate(take, line.Item.Sku);

                    var allocation = new InventoryAllocation
                    {
                        OrderDetailId = line.Id,
                        InventoryBalanceId = balance.Id,
                        LocationId = balance.LocationId,
                        ItemId = balance.ItemId,
                        LotId = balance.LotId,
                        SerialId = balance.SerialId,
                        LicensePlateId = balance.LicensePlateId,
                        AllocatedQuantity = take,
                        PickedQuantity = 0m,
                        // Candidates arrive FEFO-ordered; recorded so a future algorithm
                        // can be compared against today's behaviour (doc §10).
                        AllocationStrategy = AllocationStrategy.Fefo,
                        Status = AllocationStatus.Allocated
                    };

                    // Added through the DbSet, not just the navigation collection: the
                    // parent line is already persisted, and BaseEntity pre-assigns Id,
                    // so navigation-only discovery would be classified Modified.
                    _db.InventoryAllocations.Add(allocation);
                    created.Add(allocation);

                    line.AllocatedQuantity += take;
                    remaining -= take;

                    // Rule §11.9 - reserving stock is a stock change and is journalled.
                    _ledger.RecordNonPhysical(new StockMutation
                    {
                        WarehouseId = order.WarehouseId,
                        ItemId = line.ItemId,
                        Quantity = take,
                        TransactionType = InventoryTransactionType.Allocation,
                        ReferenceType = TransactionReferenceType.Allocation,
                        ReferenceId = allocation.Id,
                        CorrelationId = orderId,
                        FromLocationId = balance.LocationId,
                        ToLocationId = balance.LocationId,
                        FromInventoryStatusId = balance.InventoryStatusId,
                        ToInventoryStatusId = balance.InventoryStatusId,
                        LotId = balance.LotId,
                        SerialId = balance.SerialId,
                        LicensePlateId = balance.LicensePlateId,
                        Notes = $"Allocated to order {order.OrderNumber} line {line.LineNumber}"
                    });
                }

                if (remaining > 0)
                {
                    shortfalls.Add(new ShortfallDto(
                        line.Id, line.LineNumber, line.ItemId, line.Item.Sku,
                        line.OrderedQuantity, line.AllocatedQuantity, remaining));
                }

                line.RecalculateStatus();
            }

            // Rule §11.1 in its strict form: reject the whole run rather than leave the
            // order partially allocated.
            if (shortfalls.Count > 0 && !allowPartial)
            {
                throw new InsufficientStockException(shortfalls
                    .Select(s => new StockShortfall(
                        s.ItemId, s.Sku, s.RequestedQuantity, s.AllocatedQuantity))
                    .ToList());
            }

            // Acceptance scenario 3: this can only reach Allocated when every line is covered.
            order.RecalculateAllocationStatus();

            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Allocated order {OrderNumber}: {AllocationCount} allocations, {ShortfallCount} shortfalls, status {Status}",
                order.OrderNumber, created.Count, shortfalls.Count, order.Status);

            var ids = created.Select(a => a.Id).ToList();
            var dtos = await LoadAllocationsAsync(ids, token);

            return new AllocationResultDto(
                order.Id, order.Status,
                shortfalls.Count == 0 && order.Status == OrderStatus.Allocated,
                dtos, shortfalls);
        }, ct);
    }

    /// <summary>
    /// Releases reservations back to available stock. On-hand is untouched throughout,
    /// which is the mirror of rule §11.2.
    /// </summary>
    public async Task<AllocationResultDto> DeallocateAsync(
        Guid orderId, IReadOnlyList<Guid>? allocationIds, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var order = await _db.Orders
                .Include(o => o.Details).ThenInclude(d => d.Allocations)
                .Include(o => o.Details).ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(OrderMaster), orderId);

            var targets = order.Details
                .SelectMany(d => d.Allocations)
                .Where(a => a.Status == AllocationStatus.Allocated)
                .Where(a => allocationIds is null || allocationIds.Contains(a.Id))
                .ToList();

            if (targets.Count == 0)
                throw new BusinessRuleViolationException("There are no open allocations to release.");

            foreach (var allocation in targets)
            {
                var open = allocation.OpenQuantity;
                if (open <= 0) continue;

                var balance = await _inventory.GetBalanceForUpdateAsync(allocation.InventoryBalanceId, token)
                    ?? throw new NotFoundException("InventoryBalance", allocation.InventoryBalanceId);

                balance.Deallocate(open);

                var line = order.Details.First(d => d.Id == allocation.OrderDetailId);
                line.AllocatedQuantity -= open;
                allocation.AllocatedQuantity -= open;

                if (allocation.AllocatedQuantity <= 0)
                {
                    allocation.Status = AllocationStatus.Cancelled;
                    _db.InventoryAllocations.Remove(allocation);
                }

                _ledger.RecordNonPhysical(new StockMutation
                {
                    WarehouseId = order.WarehouseId,
                    ItemId = allocation.ItemId,
                    Quantity = open,
                    TransactionType = InventoryTransactionType.Deallocation,
                    ReferenceType = TransactionReferenceType.Allocation,
                    ReferenceId = allocation.Id,
                    CorrelationId = orderId,
                    FromLocationId = balance.LocationId,
                    ToLocationId = balance.LocationId,
                    FromInventoryStatusId = balance.InventoryStatusId,
                    ToInventoryStatusId = balance.InventoryStatusId,
                    LotId = balance.LotId,
                    SerialId = balance.SerialId,
                    LicensePlateId = balance.LicensePlateId,
                    Notes = $"Deallocated from order {order.OrderNumber}"
                });

                line.RecalculateStatus();
            }

            order.RecalculateAllocationStatus();
            await _db.SaveChangesAsync(token);

            return new AllocationResultDto(
                order.Id, order.Status, false, Array.Empty<AllocationDto>(), Array.Empty<ShortfallDto>());
        }, ct);
    }

    private async Task<IReadOnlyList<AllocationDto>> LoadAllocationsAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
        => await _db.InventoryAllocations
            .Where(a => ids.Contains(a.Id))
            .Select(a => new AllocationDto(
                a.Id, a.OrderDetailId, a.InventoryBalanceId,
                a.LocationId, a.Location.Code,
                a.ItemId, a.Item.Sku,
                a.LotId, a.Lot != null ? a.Lot.LotNumber : null,
                a.Lot != null ? a.Lot.ExpirationDate : null,
                a.SerialId, a.Serial != null ? a.Serial.Serial : null,
                a.LicensePlateId,
                a.AllocatedQuantity, a.PickedQuantity,
                a.AllocationStrategy, a.Status))
            .ToListAsync(ct);
}
