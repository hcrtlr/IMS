using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Application.Features.Inventory;
using IMS.Domain.Entities.Outbound;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IMS.Application.Features.Outbound;

/// <summary>
/// Doc §7.4 - picking. One task per allocation, so a picker is always told exactly which
/// stock to take from which location.
///
/// Completing a pick moves the stock to the destination while keeping it reserved: on-hand
/// and allocated fall at the source and rise at the destination together. The stock is
/// therefore never available for a different order while it sits in staging, and on-hand
/// is unchanged in total until shipment (rule §11.3).
/// </summary>
public class PickingService
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryRepository _inventory;
    private readonly InventoryLedger _ledger;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<PickingService> _logger;

    public PickingService(
        IApplicationDbContext db,
        IInventoryRepository inventory,
        InventoryLedger ledger,
        ScopeGuard scope,
        IDateTimeProvider clock,
        ILogger<PickingService> logger)
    {
        _db = db;
        _inventory = inventory;
        _ledger = ledger;
        _scope = scope;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>POST /api/orders/{id}/create-pick-tasks (§12).</summary>
    public async Task<IReadOnlyList<PickTaskDto>> CreatePickTasksAsync(
        Guid orderId, CreatePickTasksRequest? request, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var order = await _db.Orders
                .Include(o => o.Details).ThenInclude(d => d.Allocations)
                .Include(o => o.PickTasks)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(OrderMaster), orderId);

            if (order.Status is not (OrderStatus.Allocated
                or OrderStatus.PartiallyAllocated or OrderStatus.Picking))
            {
                throw new BusinessRuleViolationException(
                    $"Pick tasks can only be created for an allocated order (current status: {order.Status}).");
            }

            if (request?.DestinationLocationId is not null)
                await _scope.EnsureLocationAsync(request.DestinationLocationId.Value, order.WarehouseId, token);

            // Allocations that already have an open task must not get a second one.
            var covered = order.PickTasks
                .Where(t => t.Status != PickTaskStatus.Cancelled)
                .Select(t => t.AllocationId)
                .ToHashSet();

            var pending = order.Details
                .SelectMany(d => d.Allocations)
                .Where(a => a.Status == AllocationStatus.Allocated && !covered.Contains(a.Id))
                .ToList();

            if (pending.Count == 0)
                throw new BusinessRuleViolationException(
                    "Every allocation on this order already has a pick task.");

            // Doc §7.4 SequenceNumber, seeded from the location's pick sequence so the
            // task list already reflects a sensible walk order. A future routing
            // algorithm will own PickBatchId and this sequence (doc §10).
            var pickSequences = await _db.Locations
                .Where(l => l.WarehouseId == order.WarehouseId)
                .Select(l => new { l.Id, l.PickSequence })
                .ToDictionaryAsync(x => x.Id, x => x.PickSequence ?? int.MaxValue, token);

            var created = new List<PickTask>();
            var sequence = 1;

            foreach (var allocation in pending
                         .OrderBy(a => pickSequences.GetValueOrDefault(a.LocationId, int.MaxValue)))
            {
                var task = new PickTask
                {
                    WarehouseId = order.WarehouseId,
                    OrderId = order.Id,
                    OrderDetailId = allocation.OrderDetailId,
                    AllocationId = allocation.Id,
                    ItemId = allocation.ItemId,
                    FromLocationId = allocation.LocationId,
                    DestinationLocationId = request?.DestinationLocationId,
                    Quantity = allocation.OpenQuantity,
                    PickedQuantity = 0m,
                    SequenceNumber = sequence++,
                    Status = request?.AssignTo is null ? PickTaskStatus.Created : PickTaskStatus.Assigned,
                    AssignedTo = request?.AssignTo
                };

                _db.PickTasks.Add(task);
                created.Add(task);
            }

            order.TransitionTo(OrderStatus.Picking);
            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Created {Count} pick tasks for order {OrderNumber}", created.Count, order.OrderNumber);

            return await LoadTasksAsync(created.Select(t => t.Id).ToList(), token);
        }, ct);
    }

    /// <summary>
    /// POST /api/pick-tasks/{id}/complete (§12).
    ///
    /// Picking less than requested is a legitimate short pick: the task closes as
    /// ShortPicked and the unpicked remainder is released back to available stock so it
    /// is not stranded on a reservation nobody will fulfil.
    /// </summary>
    public async Task<PickTaskDto> CompletePickAsync(
        Guid taskId, CompletePickRequest request, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var task = await _db.PickTasks
                .Include(t => t.Allocation)
                .Include(t => t.OrderDetail).ThenInclude(d => d.Item)
                .Include(t => t.Order)
                .FirstOrDefaultAsync(
                    t => t.Id == taskId && t.Order.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(PickTask), taskId);

            if (task.Status is PickTaskStatus.Completed
                or PickTaskStatus.ShortPicked or PickTaskStatus.Cancelled)
            {
                throw new InvalidStateTransitionException(
                    nameof(PickTask), task.Status.ToString(), nameof(PickTaskStatus.Completed));
            }

            var picked = request.PickedQuantity ?? task.Quantity;

            if (picked < 0 || picked > task.Quantity)
                throw new BusinessRuleViolationException(
                    $"Picked quantity must be between 0 and the task quantity of {task.Quantity}.");

            var allocation = task.Allocation;
            var line = task.OrderDetail;
            var sku = line.Item.Sku;
            var correlationId = Guid.NewGuid();

            var source = await _inventory.GetBalanceForUpdateAsync(allocation.InventoryBalanceId, token)
                ?? throw new NotFoundException("InventoryBalance", allocation.InventoryBalanceId);

            if (picked > 0)
            {
                if (task.DestinationLocationId is Guid destinationId
                    && destinationId != source.LocationId)
                {
                    // Move to staging, keeping the stock reserved for this order.
                    source.ShipAllocated(picked, sku);

                    var destination = await _inventory.GetOrCreateBalanceForUpdateAsync(
                        new BalanceKey(
                            source.WarehouseId, destinationId, source.ItemId,
                            source.InventoryStatusId, source.LotId, source.SerialId,
                            source.LicensePlateId),
                        source.ReceivedAt,
                        token);

                    destination.AddAllocatedStock(picked);

                    // The reservation now points at the staging balance, so shipment
                    // consumes the stock from where it actually is.
                    allocation.InventoryBalanceId = destination.Id;
                    allocation.LocationId = destinationId;

                    await _inventory.RemoveEmptyBalanceAsync(source, token);

                    _ledger.RecordNonPhysical(new StockMutation
                    {
                        WarehouseId = task.WarehouseId,
                        ItemId = task.ItemId,
                        Quantity = picked,
                        TransactionType = InventoryTransactionType.Pick,
                        ReferenceType = TransactionReferenceType.PickTask,
                        ReferenceId = task.Id,
                        CorrelationId = correlationId,
                        FromLocationId = task.FromLocationId,
                        ToLocationId = destinationId,
                        FromInventoryStatusId = source.InventoryStatusId,
                        ToInventoryStatusId = source.InventoryStatusId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        LicensePlateId = source.LicensePlateId,
                        Notes = request.Notes ?? $"Picked for order {task.Order.OrderNumber}"
                    });
                }
                else
                {
                    // No staging location: the stock stays put, still reserved. Only the
                    // pick progress is journalled.
                    _ledger.RecordNonPhysical(new StockMutation
                    {
                        WarehouseId = task.WarehouseId,
                        ItemId = task.ItemId,
                        Quantity = picked,
                        TransactionType = InventoryTransactionType.Pick,
                        ReferenceType = TransactionReferenceType.PickTask,
                        ReferenceId = task.Id,
                        CorrelationId = correlationId,
                        FromLocationId = task.FromLocationId,
                        ToLocationId = task.FromLocationId,
                        FromInventoryStatusId = source.InventoryStatusId,
                        ToInventoryStatusId = source.InventoryStatusId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        LicensePlateId = source.LicensePlateId,
                        Notes = request.Notes ?? $"Picked for order {task.Order.OrderNumber}"
                    });
                }

                allocation.PickedQuantity += picked;
                line.PickedQuantity += picked;
            }

            var shortQuantity = task.Quantity - picked;

            if (shortQuantity > 0)
            {
                // The unpicked remainder is still reserved on the SOURCE balance - only the
                // picked quantity moved to staging - so it is released back there.
                source.Deallocate(shortQuantity);

                _ledger.RecordNonPhysical(new StockMutation
                {
                    WarehouseId = task.WarehouseId,
                    ItemId = task.ItemId,
                    Quantity = shortQuantity,
                    TransactionType = InventoryTransactionType.Deallocation,
                    ReferenceType = TransactionReferenceType.PickTask,
                    ReferenceId = task.Id,
                    CorrelationId = correlationId,
                    FromLocationId = task.FromLocationId,
                    ToLocationId = task.FromLocationId,
                    FromInventoryStatusId = source.InventoryStatusId,
                    ToInventoryStatusId = source.InventoryStatusId,
                    LotId = source.LotId,
                    SerialId = source.SerialId,
                    LicensePlateId = source.LicensePlateId,
                    Notes = $"Short pick: {shortQuantity} released back to available"
                });

                allocation.AllocatedQuantity -= shortQuantity;
                line.AllocatedQuantity -= shortQuantity;
            }

            task.PickedQuantity = picked;
            task.CompletedAt = _clock.UtcNow;
            task.Notes = request.Notes;
            task.TransitionTo(shortQuantity > 0 ? PickTaskStatus.ShortPicked : PickTaskStatus.Completed);

            allocation.Status = allocation.OpenQuantity <= 0
                ? AllocationStatus.Picked
                : AllocationStatus.Allocated;

            line.RecalculateStatus();

            // The order becomes Picked once every open task is done.
            var openTasks = await _db.PickTasks.CountAsync(
                t => t.OrderId == task.OrderId
                     && t.Id != task.Id
                     && (t.Status == PickTaskStatus.Created
                         || t.Status == PickTaskStatus.Assigned
                         || t.Status == PickTaskStatus.InProgress), token);

            if (openTasks == 0)
            {
                var order = await _db.Orders
                    .Include(o => o.Details)
                    .FirstAsync(o => o.Id == task.OrderId, token);

                order.RecalculatePickStatus();
            }

            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Pick task {TaskId} completed: {Picked}/{Requested} ({Status})",
                task.Id, picked, task.Quantity, task.Status);

            return (await LoadTasksAsync(new[] { task.Id }, token)).Single();
        }, ct);
    }

    public async Task<PagedResult<PickTaskDto>> ListAsync(
        PickTaskQuery query, CancellationToken ct = default)
    {
        var q = _db.PickTasks.Where(t => t.Order.AccountId == _scope.AccountId);

        if (query.WarehouseId.HasValue) q = q.Where(t => t.WarehouseId == query.WarehouseId.Value);
        if (query.OrderId.HasValue) q = q.Where(t => t.OrderId == query.OrderId.Value);
        if (query.Status.HasValue) q = q.Where(t => t.Status == query.Status.Value);
        if (!string.IsNullOrWhiteSpace(query.AssignedTo)) q = q.Where(t => t.AssignedTo == query.AssignedTo);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(t => t.Status).ThenBy(t => t.SequenceNumber)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);

        return new PagedResult<PickTaskDto>(items, total, query.Page, query.PageSize);
    }

    private async Task<IReadOnlyList<PickTaskDto>> LoadTasksAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
        => await _db.PickTasks
            .Where(t => ids.Contains(t.Id))
            .OrderBy(t => t.SequenceNumber)
            .Select(ToDto)
            .ToListAsync(ct);

    /// <summary>Expression-shaped so EF can translate it inside Select.</summary>
    private static readonly System.Linq.Expressions.Expression<Func<PickTask, PickTaskDto>> ToDto =
        t => new PickTaskDto(
            t.Id, t.WarehouseId,
            t.OrderId, t.Order.OrderNumber,
            t.OrderDetailId, t.AllocationId,
            t.ItemId, t.Item.Sku, t.Item.Name,
            t.FromLocationId, t.FromLocation.Code,
            t.DestinationLocationId, t.DestinationLocation != null ? t.DestinationLocation.Code : null,
            t.Quantity, t.PickedQuantity, t.Quantity - t.PickedQuantity,
            t.SequenceNumber, t.PickBatchId,
            t.Status, t.AssignedTo,
            t.Allocation.Lot != null ? t.Allocation.Lot.LotNumber : null,
            t.Allocation.Serial != null ? t.Allocation.Serial.Serial : null,
            t.StartedAt, t.CompletedAt, t.Notes);
}
