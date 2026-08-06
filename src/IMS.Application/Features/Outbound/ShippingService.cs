using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Services;
using IMS.Application.Features.Inventory;
using IMS.Domain.Entities.Algorithms;
using IMS.Domain.Entities.Outbound;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IMS.Application.Features.Outbound;

/// <summary>
/// Shipment - the point where stock leaves the building.
///
/// Doc §7.4: "Shipping tamamlandiginda ise stok sistemden dusurulmelidir."
/// Rule §11.3: shipment reduces BOTH on-hand and allocated quantity.
///
/// Shipping also writes the Faz 6 OrderHistorySnapshot rows, which are the demand history
/// a future slotting or batching algorithm needs (doc §10).
/// </summary>
public class ShippingService
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryRepository _inventory;
    private readonly InventoryLedger _ledger;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ShippingService> _logger;

    public ShippingService(
        IApplicationDbContext db,
        IInventoryRepository inventory,
        InventoryLedger ledger,
        ScopeGuard scope,
        IDateTimeProvider clock,
        ILogger<ShippingService> logger)
    {
        _db = db;
        _inventory = inventory;
        _ledger = ledger;
        _scope = scope;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>POST /api/orders/{id}/ship (§12).</summary>
    public async Task<ShipmentDto> ShipAsync(
        Guid orderId, ShipRequest? request, CancellationToken ct = default)
    {
        return await _db.ExecuteInTransactionAsync(async token =>
        {
            var order = await _db.Orders
                .Include(o => o.Details).ThenInclude(d => d.Item)
                .Include(o => o.Details).ThenInclude(d => d.Allocations)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.AccountId == _scope.AccountId, token)
                ?? throw new NotFoundException(nameof(OrderMaster), orderId);

            // A terminal order is a state conflict (409), not a rule violation: the caller
            // is repeating a completed action rather than asking for something disallowed.
            if (order.Status is OrderStatus.Shipped or OrderStatus.Cancelled)
                throw new InvalidStateTransitionException(
                    nameof(OrderMaster), order.Status.ToString(), nameof(OrderStatus.Shipped));

            // The lifecycle (§7.1) requires picking before shipping. TransitionTo would
            // reject anything earlier, but failing here gives a clearer message.
            if (order.Status is not (OrderStatus.Picked or OrderStatus.Packed))
                throw new BusinessRuleViolationException(
                    $"An order must be Picked or Packed before shipping (current status: {order.Status}).");

            var picked = order.Details
                .SelectMany(d => d.Allocations)
                .Where(a => a.Status == AllocationStatus.Picked && a.PickedQuantity > 0)
                .ToList();

            if (picked.Count == 0)
                throw new BusinessRuleViolationException("There is nothing picked to ship on this order.");

            var correlationId = Guid.NewGuid();

            var shipment = new Shipment
            {
                WarehouseId = order.WarehouseId,
                OrderId = order.Id,
                ShipmentNumber = await GenerateShipmentNumberAsync(token),
                Carrier = request?.Carrier ?? order.Carrier,
                ServiceLevel = request?.ServiceLevel ?? order.ServiceLevel,
                TrackingNumber = request?.TrackingNumber,
                ShippedAt = _clock.UtcNow,
                ShippedBy = _scope.Username,
                CorrelationId = correlationId
            };

            _db.Shipments.Add(shipment);

            decimal totalWeight = 0m, totalVolume = 0m;

            foreach (var allocation in picked)
            {
                var line = order.Details.First(d => d.Id == allocation.OrderDetailId);
                var quantity = allocation.PickedQuantity;

                var balance = await _inventory.GetBalanceForUpdateAsync(allocation.InventoryBalanceId, token)
                    ?? throw new NotFoundException("InventoryBalance", allocation.InventoryBalanceId);

                // Rule §11.3 - reduces on-hand AND allocated, and refuses to go negative.
                balance.ShipAllocated(quantity, line.Item.Sku);

                var shipmentLine = new ShipmentLine
                {
                    ShipmentId = shipment.Id,
                    OrderDetailId = line.Id,
                    ItemId = line.ItemId,
                    LotId = allocation.LotId,
                    SerialId = allocation.SerialId,
                    LicensePlateId = allocation.LicensePlateId,
                    Quantity = quantity
                };

                _db.ShipmentLines.Add(shipmentLine);
                shipment.Lines.Add(shipmentLine);

                _ledger.RecordNonPhysical(new StockMutation
                {
                    WarehouseId = order.WarehouseId,
                    ItemId = line.ItemId,
                    Quantity = quantity,
                    TransactionType = InventoryTransactionType.Ship,
                    ReferenceType = TransactionReferenceType.Shipment,
                    ReferenceId = shipment.Id,
                    CorrelationId = correlationId,
                    // Stock leaves the warehouse: there is no destination location.
                    FromLocationId = balance.LocationId,
                    ToLocationId = null,
                    FromInventoryStatusId = balance.InventoryStatusId,
                    ToInventoryStatusId = null,
                    LotId = allocation.LotId,
                    SerialId = allocation.SerialId,
                    LicensePlateId = allocation.LicensePlateId,
                    Notes = $"Shipped on {shipment.ShipmentNumber}"
                });

                line.ShippedQuantity += quantity;
                allocation.Status = AllocationStatus.Shipped;
                line.RecalculateStatus();

                totalWeight += (line.Item.Weight ?? 0m) * quantity;
                totalVolume += (line.Item.Volume ?? 0m) * quantity;

                // A shipped serial is no longer in the warehouse.
                if (allocation.SerialId is Guid serialId)
                {
                    var serial = await _db.SerialNumbers.FirstAsync(s => s.Id == serialId, token);
                    serial.Status = SerialStatus.Shipped;
                }

                await _inventory.RemoveEmptyBalanceAsync(balance, token);
            }

            shipment.TotalWeight = totalWeight;
            shipment.TotalVolume = totalVolume;

            order.TransitionTo(OrderStatus.Shipped);
            order.ShippedAt = shipment.ShippedAt;

            await WriteOrderHistoryAsync(order, shipment, token);

            await _db.SaveChangesAsync(token);

            _logger.LogInformation(
                "Order {OrderNumber} shipped as {ShipmentNumber}: {LineCount} lines, correlation {CorrelationId}",
                order.OrderNumber, shipment.ShipmentNumber, shipment.Lines.Count, correlationId);

            return await GetShipmentAsync(shipment.Id, token);
        }, ct);
    }

    /// <summary>
    /// Faz 6 "Historical order verisi". One denormalised row per shipped line, so demand
    /// history survives purging of the live order tables and stays readable if a SKU is
    /// later renamed. Nothing reads these yet - doc §10 defers the algorithms.
    /// </summary>
    private async Task WriteOrderHistoryAsync(
        OrderMaster order, Shipment shipment, CancellationToken ct)
    {
        var lineCount = order.Details.Count(d => d.Status != OrderDetailStatus.Cancelled);

        foreach (var line in order.Details.Where(d => d.ShippedQuantity > 0))
        {
            var alreadyRecorded = await _db.OrderHistory
                .AnyAsync(h => h.OrderDetailId == line.Id, ct);

            if (alreadyRecorded) continue;

            // Where the stock was finally picked from, for future travel analysis.
            var pickedFrom = line.Allocations
                .OrderByDescending(a => a.PickedQuantity)
                .Select(a => (Guid?)a.LocationId)
                .FirstOrDefault();

            _db.OrderHistory.Add(new OrderHistorySnapshot
            {
                AccountId = order.AccountId,
                WarehouseId = order.WarehouseId,
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                OrderDetailId = line.Id,
                ItemId = line.ItemId,
                Sku = line.Item.Sku,
                CustomerId = order.CustomerId,
                OrderType = order.OrderType,
                Priority = order.Priority,
                Carrier = shipment.Carrier,
                ServiceLevel = shipment.ServiceLevel,
                OrderDate = order.OrderDate,
                RequiredShipDate = order.RequiredShipDate,
                ShippedAt = shipment.ShippedAt,
                OrderedQuantity = line.OrderedQuantity,
                ShippedQuantity = line.ShippedQuantity,
                OrderLineCount = lineCount,
                PickedFromLocationId = pickedFrom,
                FulfillmentDurationSeconds =
                    (int)(shipment.ShippedAt - order.OrderDate).TotalSeconds
            });
        }
    }

    public async Task<ShipmentDto> GetShipmentAsync(Guid id, CancellationToken ct = default)
    {
        var shipment = await _db.Shipments
            .Include(s => s.Order)
            .Include(s => s.Lines).ThenInclude(l => l.Item)
            .FirstOrDefaultAsync(s => s.Id == id && s.Order.AccountId == _scope.AccountId, ct)
            ?? throw new NotFoundException(nameof(Shipment), id);

        var lotNumbers = await _db.Lots
            .Where(l => shipment.Lines.Select(x => x.LotId).Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.LotNumber, ct);

        return new ShipmentDto(
            shipment.Id, shipment.ShipmentNumber,
            shipment.OrderId, shipment.Order.OrderNumber,
            shipment.Carrier, shipment.ServiceLevel, shipment.TrackingNumber,
            shipment.ShippedAt, shipment.ShippedBy,
            shipment.TotalWeight, shipment.TotalVolume, shipment.CorrelationId,
            shipment.Lines.Select(l => new ShipmentLineDto(
                l.Id, l.OrderDetailId, l.ItemId, l.Item?.Sku ?? string.Empty,
                l.LotId, l.LotId is Guid lid ? lotNumbers.GetValueOrDefault(lid) : null,
                l.SerialId, l.Quantity)).ToList());
    }

    public async Task<IReadOnlyList<ShipmentDto>> ListForOrderAsync(
        Guid orderId, CancellationToken ct = default)
    {
        var ids = await _db.Shipments
            .Where(s => s.OrderId == orderId && s.Order.AccountId == _scope.AccountId)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var result = new List<ShipmentDto>();
        foreach (var id in ids) result.Add(await GetShipmentAsync(id, ct));
        return result;
    }

    private async Task<string> GenerateShipmentNumberAsync(CancellationToken ct)
    {
        var prefix = $"SH-{_clock.UtcNow:yyyyMMdd}-";

        var last = await _db.Shipments
            .Where(s => s.ShipmentNumber.StartsWith(prefix))
            .OrderByDescending(s => s.ShipmentNumber)
            .Select(s => s.ShipmentNumber)
            .FirstOrDefaultAsync(ct);

        var next = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
        return prefix + next.ToString("D4");
    }
}
