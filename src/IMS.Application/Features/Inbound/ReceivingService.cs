using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Application.Features.Inventory;
using IMS.Domain.Entities.Inbound;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IMS.Application.Features.Inbound;

/// <summary>
/// Doc §6.3 and §6.4 - physical receipt and putaway.
///
/// Receiving performs the six steps the doc lists: verify the item, capture quantity,
/// capture lot/serial, create an LPN if needed, add stock to the receiving location, and
/// write an inventory transaction. All of it happens in one database transaction so a
/// partial receipt can never be persisted (rule §11.12).
/// </summary>
public class ReceivingService
{
    private readonly IApplicationDbContext _db;
    private readonly InventoryLedger _ledger;
    private readonly InventoryService _inventoryService;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ReceivingService> _logger;

    public ReceivingService(
        IApplicationDbContext db,
        InventoryLedger ledger,
        InventoryService inventoryService,
        ScopeGuard scope,
        IDateTimeProvider clock,
        ILogger<ReceivingService> logger)
    {
        _db = db;
        _ledger = ledger;
        _inventoryService = inventoryService;
        _scope = scope;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/inbound-orders/{id}/receive (§12).
    /// </summary>
    public async Task<ReceiptDto> ReceiveAsync(
        Guid inboundOrderId, ReceiveRequest request, CancellationToken ct = default)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            throw new BusinessRuleViolationException("A receipt must contain at least one line.");

        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var order = await _db.InboundOrders
                .Include(o => o.Details).ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(o => o.Id == inboundOrderId && o.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(InboundOrder), inboundOrderId);

            if (order.Status is InboundOrderStatus.Draft)
                throw new BusinessRuleViolationException(
                    "This inbound order is still a draft. Confirm it as Expected before receiving.");

            if (order.Status is InboundOrderStatus.Cancelled or InboundOrderStatus.Completed)
                throw new InvalidStateTransitionException(
                    nameof(InboundOrder), order.Status.ToString(), "Received");

            await _scope.EnsureLocationAsync(request.ReceivingLocationId, order.WarehouseId, token);

            // Doc §6.3 puts received stock in a receiving location; enforcing the zone type
            // keeps the inbound flow honest rather than letting stock land anywhere.
            var receivingZoneType = await _db.Locations
                .Where(l => l.Id == request.ReceivingLocationId)
                .Select(l => l.Zone.ZoneType)
                .FirstAsync(token);

            if (receivingZoneType != ZoneType.Receiving)
                throw new BusinessRuleViolationException(
                    "Stock must be received into a location inside a Receiving zone.");

            var correlationId = Guid.NewGuid();

            var receipt = new Receipt
            {
                WarehouseId = order.WarehouseId,
                InboundOrderId = order.Id,
                ReceiptNumber = await GenerateReceiptNumberAsync(token),
                ReceivingLocationId = request.ReceivingLocationId,
                ReceivedAt = _clock.UtcNow,
                ReceivedBy = _scope.Username,
                Notes = request.Notes,
                CorrelationId = correlationId
            };

            _db.Receipts.Add(receipt);

            var defaultStatusId = await _db.InventoryStatuses
                .Where(s => s.Code == InventoryStatus.Available)
                .Select(s => s.Id).FirstAsync(token);

            foreach (var line in request.Lines)
            {
                var detail = order.Details.FirstOrDefault(d => d.Id == line.InboundOrderDetailId)
                    ?? throw new NotFoundException(
                        $"Inbound order line '{line.InboundOrderDetailId}' is not on this order.");

                if (line.ReceivedQuantity <= 0)
                    throw new BusinessRuleViolationException(
                        $"Line {detail.LineNumber}: received quantity must be greater than zero.");

                var item = detail.Item;

                // Normalise to base UOM so all stock maths shares one unit.
                var uomId = line.ReceivedUomId ?? detail.UomId;
                var baseQuantity = await _inventoryService.ConvertToBaseAsync(
                    item.Id, uomId, line.ReceivedQuantity, token);

                // Over-receipt is refused: the expected quantity is the contract with the
                // supplier, and silently accepting more would misstate the order.
                var expectedBase = await _inventoryService.ConvertToBaseAsync(
                    item.Id, detail.UomId, detail.ExpectedQuantity, token);

                if (detail.ReceivedQuantity + baseQuantity > expectedBase)
                    throw new BusinessRuleViolationException(
                        $"Line {detail.LineNumber}: receiving {baseQuantity} would exceed the " +
                        $"expected quantity of {expectedBase} (already received " +
                        $"{detail.ReceivedQuantity}).");

                // Doc §6.3 - capture lot / serial, applying rules §11.5 and §11.6.
                var lotId = await _inventoryService.ResolveLotAsync(
                    item,
                    line.LotNumber ?? detail.ExpectedLotNumber,
                    line.ManufactureDate,
                    line.ExpirationDate ?? detail.ExpectedExpirationDate,
                    line.SupplierLotNumber,
                    token);

                var serialId = await _inventoryService.ResolveSerialAsync(
                    item, line.SerialNumber, lotId, token);

                if (line.LicensePlateId.HasValue)
                {
                    var lpnOk = await _db.LicensePlates.AnyAsync(
                        lp => lp.Id == line.LicensePlateId.Value
                              && lp.WarehouseId == order.WarehouseId, token);

                    if (!lpnOk) throw new NotFoundException("LicensePlate", line.LicensePlateId.Value);
                }

                var statusId = line.InventoryStatusId ?? defaultStatusId;

                var receiptLine = new ReceiptLine
                {
                    ReceiptId = receipt.Id,
                    InboundOrderDetailId = detail.Id,
                    ItemId = item.Id,
                    ReceivedQuantity = line.ReceivedQuantity,
                    ReceivedUomId = uomId,
                    BaseQuantity = baseQuantity,
                    LotId = lotId,
                    SerialId = serialId,
                    LicensePlateId = line.LicensePlateId,
                    InventoryStatusId = statusId,
                    PutawayQuantity = 0m
                };

                // Add through the DbSet, not just the navigation collection.
                //
                // BaseEntity pre-assigns Id, so a new child discovered only through a
                // navigation on an already-saved parent is classified Modified rather than
                // Added, producing an UPDATE against a non-existent row. The parent IS
                // already saved here whenever the item is lot tracked, because creating the
                // lot flushes the receipt first.
                _db.ReceiptLines.Add(receiptLine);
                receipt.Lines.Add(receiptLine);

                // Doc §6.3 - stock lands in the receiving location and a transaction is written.
                await _ledger.AddAsync(new StockMutation
                {
                    WarehouseId = order.WarehouseId,
                    ItemId = item.Id,
                    Quantity = baseQuantity,
                    TransactionType = InventoryTransactionType.Receipt,
                    ReferenceType = TransactionReferenceType.Receipt,
                    ReferenceId = receipt.Id,
                    CorrelationId = correlationId,
                    ToLocationId = request.ReceivingLocationId,
                    ToInventoryStatusId = statusId,
                    LotId = lotId,
                    SerialId = serialId,
                    LicensePlateId = line.LicensePlateId,
                    Notes = $"Receipt {receipt.ReceiptNumber} line {detail.LineNumber}"
                }, token);

                detail.ReceivedQuantity += baseQuantity;
            }

            // Doc §6.1 - header status follows line progress.
            order.RecalculateStatus();

            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Receipt {ReceiptNumber} against inbound order {OrderNumber}: {LineCount} lines, correlation {CorrelationId}",
                receipt.ReceiptNumber, order.OrderNumber, receipt.Lines.Count, correlationId);

            return await GetReceiptAsync(receipt.Id, token);
        }, ct);
    }

    public async Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken ct = default)
    {
        var receipt = await _db.Receipts
            .Include(r => r.InboundOrder)
            .Include(r => r.ReceivingLocation)
            .Include(r => r.Lines).ThenInclude(l => l.Item)
            .Include(r => r.Lines).ThenInclude(l => l.ReceivedUom)
            .Include(r => r.Lines).ThenInclude(l => l.Lot)
            .Include(r => r.Lines).ThenInclude(l => l.Serial)
            .Include(r => r.Lines).ThenInclude(l => l.LicensePlate)
            .Include(r => r.Lines).ThenInclude(l => l.InventoryStatus)
            .FirstOrDefaultAsync(r => r.Id == id && r.Warehouse.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Receipt), id);

        return ToDto(receipt);
    }

    // ------------------------------------------------------------ putaway (§6.4)

    /// <summary>
    /// POST /api/receipts/{id}/create-putaway (§12).
    ///
    /// Creates one task per receipt line still sitting in the receiving location. Doc §6.4:
    /// in v1 the operator chooses the destination on completion. SuggestedLocationId and
    /// RecommendationReason stay null until a slotting algorithm exists.
    /// </summary>
    public async Task<IReadOnlyList<PutawayTaskDto>> CreatePutawayTasksAsync(
        Guid receiptId, CreatePutawayRequest? request, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var receipt = await _db.Receipts
                .Include(r => r.Lines).ThenInclude(l => l.Item)
                .FirstOrDefaultAsync(
                    r => r.Id == receiptId && r.Warehouse.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(Receipt), receiptId);

            // Default to every line with stock still awaiting putaway.
            var requested = request?.Lines?.ToList()
                ?? receipt.Lines
                    .Where(l => l.PendingPutawayQuantity > 0)
                    .Select(l => new CreatePutawayLineRequest(l.Id, null, null))
                    .ToList();

            if (requested.Count == 0)
                throw new BusinessRuleViolationException(
                    "Every line on this receipt has already been put away.");

            var created = new List<PutawayTask>();

            foreach (var req in requested)
            {
                var line = receipt.Lines.FirstOrDefault(l => l.Id == req.ReceiptLineId)
                    ?? throw new NotFoundException(
                        $"Receipt line '{req.ReceiptLineId}' is not on this receipt.");

                var quantity = req.Quantity ?? line.PendingPutawayQuantity;

                if (quantity <= 0)
                    throw new BusinessRuleViolationException(
                        "Putaway quantity must be greater than zero.");

                if (quantity > line.PendingPutawayQuantity)
                    throw new BusinessRuleViolationException(
                        $"Cannot put away {quantity}; only {line.PendingPutawayQuantity} " +
                        $"of this receipt line is still in the receiving location.");

                if (req.SuggestedLocationId.HasValue)
                    await _scope.EnsureLocationAsync(req.SuggestedLocationId.Value, receipt.WarehouseId, token);

                var task = new PutawayTask
                {
                    WarehouseId = receipt.WarehouseId,
                    ReceiptLineId = line.Id,
                    ItemId = line.ItemId,
                    FromLocationId = receipt.ReceivingLocationId,
                    SuggestedLocationId = req.SuggestedLocationId,
                    Quantity = quantity,
                    Status = PutawayTaskStatus.Created,
                    // Populated by the future slotting algorithm (doc §6.4).
                    RecommendationReason = null
                };

                _db.PutawayTasks.Add(task);
                created.Add(task);

                // Reserve the quantity against the line so a second call cannot double-book it.
                line.PutawayQuantity += quantity;
            }

            await _db.SaveChangesAsync(token);

            var ids = created.Select(t => t.Id).ToList();
            return await LoadPutawayTasksAsync(ids, token);
        }, ct);
    }

    /// <summary>
    /// POST /api/putaway-tasks/{id}/complete (§12).
    ///
    /// Moves the stock from the receiving location to the destination the operator chose,
    /// writing a Putaway transaction. Rule §11.8 is checked before the move.
    /// </summary>
    public async Task<PutawayTaskDto> CompletePutawayAsync(
        Guid taskId, CompletePutawayRequest request, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var task = await _db.PutawayTasks
                .Include(t => t.ReceiptLine)
                .FirstOrDefaultAsync(
                    t => t.Id == taskId
                         && _db.Warehouses.Any(w => w.Id == t.WarehouseId && w.AccountId == _scope.AccountId),
                    token)
                ?? throw new NotFoundException(nameof(PutawayTask), taskId);

            if (task.Status is PutawayTaskStatus.Completed or PutawayTaskStatus.Cancelled)
                throw new InvalidStateTransitionException(
                    nameof(PutawayTask), task.Status.ToString(), nameof(PutawayTaskStatus.Completed));

            var quantity = request.Quantity ?? task.Quantity;

            if (quantity <= 0 || quantity > task.Quantity)
                throw new BusinessRuleViolationException(
                    $"Putaway quantity must be between 0 and the task quantity of {task.Quantity}.");

            await _scope.EnsureLocationAsync(request.ActualLocationId, task.WarehouseId, token);

            var destination = await _db.Locations
                .Include(l => l.LocationProfile)
                .Include(l => l.Zone)
                .FirstAsync(l => l.Id == request.ActualLocationId, token);

            if (!destination.IsActive || !destination.IsPutawayAllowed)
                throw new BusinessRuleViolationException(
                    $"Location '{destination.Code}' does not accept putaway.");

            var item = await _db.Items.FirstAsync(i => i.Id == task.ItemId, token);

            await EnsurePutawayCompatibleAsync(destination, item, token);

            var line = task.ReceiptLine;

            await _ledger.MoveAsync(new StockMutation
            {
                WarehouseId = task.WarehouseId,
                ItemId = task.ItemId,
                Quantity = quantity,
                TransactionType = InventoryTransactionType.Putaway,
                ReferenceType = TransactionReferenceType.PutawayTask,
                ReferenceId = task.Id,
                CorrelationId = Guid.NewGuid(),
                FromLocationId = task.FromLocationId,
                ToLocationId = request.ActualLocationId,
                FromInventoryStatusId = line.InventoryStatusId,
                ToInventoryStatusId = line.InventoryStatusId,
                LotId = line.LotId,
                SerialId = line.SerialId,
                LicensePlateId = line.LicensePlateId,
                Notes = request.Notes ?? "Putaway"
            }, token);

            task.ActualLocationId = request.ActualLocationId;
            task.CompletedAt = _clock.UtcNow;
            task.TransitionTo(PutawayTaskStatus.Completed);

            // A short putaway returns the unmoved remainder to the pending pool so a new
            // task can be raised for it.
            if (quantity < task.Quantity)
            {
                line.PutawayQuantity -= task.Quantity - quantity;
                task.Quantity = quantity;
            }

            // An LPN physically travels with its stock.
            if (line.LicensePlateId.HasValue)
            {
                var lpn = await _db.LicensePlates.FirstAsync(l => l.Id == line.LicensePlateId.Value, token);
                lpn.CurrentLocationId = request.ActualLocationId;
            }

            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Putaway task {TaskId} completed: {Quantity} moved to {LocationCode}",
                task.Id, quantity, destination.Code);

            return (await LoadPutawayTasksAsync(new[] { task.Id }, token)).Single();
        }, ct);
    }

    /// <summary>
    /// Rule §11.8 - putaway must respect temperature and hazardous-material compatibility,
    /// plus the profile's item and lot mixing rules.
    /// </summary>
    private async Task EnsurePutawayCompatibleAsync(
        Domain.Entities.MasterData.Location destination,
        Domain.Entities.MasterData.ItemMaster item,
        CancellationToken ct)
    {
        if (item.IsHazardous
            && destination.LocationType != LocationType.DangerousGoods
            && destination.Zone.ZoneType != ZoneType.HazardousMaterial)
        {
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is hazardous and cannot be put away into '{destination.Code}'.",
                ruleNumber: 8);
        }

        if (item.IsTemperatureControlled)
        {
            if (destination.LocationProfile is null
                || !destination.LocationProfile.SupportsTemperatureRange(
                    item.MinimumStorageTemperature, item.MaximumStorageTemperature))
            {
                throw new BusinessRuleViolationException(
                    $"Location '{destination.Code}' does not satisfy the storage temperature " +
                    $"requirements of item '{item.Sku}'.",
                    ruleNumber: 8);
            }
        }

        var profile = destination.LocationProfile;
        if (profile is null) return;

        if (profile.AllowedItemCategoryId is not null && profile.AllowedItemCategoryId != item.CategoryId)
            throw new BusinessRuleViolationException(
                $"Location '{destination.Code}' only accepts a specific item category.",
                ruleNumber: 8);

        if (!profile.IsMixedItemAllowed)
        {
            var hasOther = await _db.InventoryBalances.AnyAsync(
                b => b.LocationId == destination.Id && b.OnHandQuantity > 0 && b.ItemId != item.Id, ct);

            if (hasOther)
                throw new BusinessRuleViolationException(
                    $"Location '{destination.Code}' does not allow mixed items and already holds another item.",
                    ruleNumber: 8);
        }
    }

    private async Task<IReadOnlyList<PutawayTaskDto>> LoadPutawayTasksAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
        => await _db.PutawayTasks
            .Where(t => ids.Contains(t.Id))
            .Select(ToDtoExpr)
            .ToListAsync(ct);

    public async Task<PagedResult<PutawayTaskDto>> ListPutawayTasksAsync(
        PutawayTaskQuery query, CancellationToken ct = default)
    {
        var q = _db.PutawayTasks.Where(
            t => _db.Warehouses.Any(w => w.Id == t.WarehouseId && w.AccountId == _scope.AccountId));

        if (query.WarehouseId.HasValue) q = q.Where(t => t.WarehouseId == query.WarehouseId.Value);
        if (query.Status.HasValue) q = q.Where(t => t.Status == query.Status.Value);
        if (query.ItemId.HasValue) q = q.Where(t => t.ItemId == query.ItemId.Value);
        if (query.ReceiptId.HasValue) q = q.Where(t => t.ReceiptLine.ReceiptId == query.ReceiptId.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderBy(t => t.Status).ThenBy(t => t.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(ToDtoExpr)
            .ToListAsync(ct);

        return new PagedResult<PutawayTaskDto>(items, total, query.Page, query.PageSize);
    }

    private async Task<string> GenerateReceiptNumberAsync(CancellationToken ct)
    {
        var prefix = $"RC-{_clock.UtcNow:yyyyMMdd}-";

        var last = await _db.Receipts
            .Where(r => r.ReceiptNumber.StartsWith(prefix))
            .OrderByDescending(r => r.ReceiptNumber)
            .Select(r => r.ReceiptNumber)
            .FirstOrDefaultAsync(ct);

        var next = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
        return prefix + next.ToString("D4");
    }

    private static ReceiptDto ToDto(Receipt r) => new(
        r.Id, r.ReceiptNumber,
        r.InboundOrderId, r.InboundOrder?.OrderNumber ?? string.Empty,
        r.WarehouseId,
        r.ReceivingLocationId, r.ReceivingLocation?.Code ?? string.Empty,
        r.ReceivedAt, r.ReceivedBy, r.Notes, r.CorrelationId,
        r.Lines.Select(l => new ReceiptLineDto(
            l.Id, l.InboundOrderDetailId,
            l.ItemId, l.Item?.Sku ?? string.Empty, l.Item?.Name ?? string.Empty,
            l.ReceivedQuantity, l.ReceivedUomId, l.ReceivedUom?.Code ?? string.Empty,
            l.BaseQuantity,
            l.LotId, l.Lot?.LotNumber, l.Lot?.ExpirationDate,
            l.SerialId, l.Serial?.Serial,
            l.LicensePlateId, l.LicensePlate?.Code,
            l.InventoryStatusId, l.InventoryStatus?.Code ?? string.Empty,
            l.PutawayQuantity, l.PendingPutawayQuantity)).ToList());

    /// <summary>
    /// Expression-shaped so EF can translate it inside Select; a method call would
    /// materialise the entity with unloaded navigations.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<PutawayTask, PutawayTaskDto>> ToDtoExpr =
        t => new PutawayTaskDto(
            t.Id, t.WarehouseId, t.ReceiptLineId,
            t.ItemId, t.Item.Sku, t.Item.Name,
            t.FromLocationId, t.FromLocation.Code,
            t.SuggestedLocationId, t.SuggestedLocation != null ? t.SuggestedLocation.Code : null,
            t.ActualLocationId, t.ActualLocation != null ? t.ActualLocation.Code : null,
            t.Quantity, t.Status, t.RecommendationReason, t.AssignedTo,
            t.ReceiptLine.LotId, t.ReceiptLine.Lot != null ? t.ReceiptLine.Lot.LotNumber : null,
            t.ReceiptLine.SerialId, t.ReceiptLine.Serial != null ? t.ReceiptLine.Serial.Serial : null,
            t.ReceiptLine.LicensePlateId,
            t.CreatedAt, t.CompletedAt);
}
