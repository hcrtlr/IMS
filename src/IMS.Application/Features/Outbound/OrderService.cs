using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.Outbound;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.Outbound;

/// <summary>
/// Doc §7.1 / §7.2 - customer order header and lines.
/// Lifecycle: Draft -> Created -> Released -> PartiallyAllocated -> Allocated ->
/// Picking -> Picked -> Packed -> Shipped -> Cancelled.
/// </summary>
public class OrderService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;

    public OrderService(IApplicationDbContext db, ScopeGuard scope, IDateTimeProvider clock)
    {
        _db = db;
        _scope = scope;
        _clock = clock;
    }

    private IQueryable<OrderMaster> Scoped()
        => _db.Orders.Where(o => o.AccountId == _scope.AccountId);

    public async Task<PagedResult<OrderSummaryDto>> ListAsync(
        OrderQuery query, CancellationToken ct = default)
    {
        var q = Scoped();

        if (query.WarehouseId.HasValue) q = q.Where(o => o.WarehouseId == query.WarehouseId.Value);
        if (query.CustomerId.HasValue) q = q.Where(o => o.CustomerId == query.CustomerId.Value);
        if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);
        if (query.OrderType.HasValue) q = q.Where(o => o.OrderType == query.OrderType.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(o => o.OrderNumber.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            // Doc §10: priority then required ship date is the order a picker would work.
            .OrderBy(o => o.Priority).ThenBy(o => o.RequiredShipDate ?? DateTimeOffset.MaxValue)
            .ThenByDescending(o => o.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(o => new OrderSummaryDto(
                o.Id, o.OrderNumber, o.WarehouseId,
                o.Customer != null ? o.Customer.Name : null,
                o.OrderType, o.Status, o.Priority,
                o.OrderDate, o.RequiredShipDate,
                o.TotalLineCount, o.TotalQuantity))
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<OrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var order = await Scoped()
            .Include(o => o.Customer)
            .Include(o => o.Details).ThenInclude(d => d.Item)
            .Include(o => o.Details).ThenInclude(d => d.Uom)
            .Include(o => o.Details).ThenInclude(d => d.Allocations).ThenInclude(a => a.Location)
            .Include(o => o.Details).ThenInclude(d => d.Allocations).ThenInclude(a => a.Lot)
            .Include(o => o.Details).ThenInclude(d => d.Allocations).ThenInclude(a => a.Serial)
            .Include(o => o.Details).ThenInclude(d => d.Allocations).ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(OrderMaster), id);

        return ToDto(order);
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);

        if (request.Lines is null || request.Lines.Count == 0)
            throw new BusinessRuleViolationException("An order must have at least one line.");

        if (request.CustomerId.HasValue)
        {
            var ok = await _db.Customers.AnyAsync(
                c => c.Id == request.CustomerId.Value && c.AccountId == _scope.AccountId, ct);

            if (!ok) throw new NotFoundException("Customer", request.CustomerId.Value);
        }

        var orderNumber = string.IsNullOrWhiteSpace(request.OrderNumber)
            ? await GenerateOrderNumberAsync(ct)
            : request.OrderNumber;

        if (await _db.Orders.AnyAsync(
                o => o.AccountId == _scope.AccountId && o.OrderNumber == orderNumber, ct))
            throw new DuplicateEntityException($"Order '{orderNumber}' already exists.");

        var order = new OrderMaster
        {
            AccountId = _scope.AccountId,
            WarehouseId = request.WarehouseId,
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            OrderDate = request.OrderDate ?? _clock.UtcNow,
            RequiredShipDate = request.RequiredShipDate,
            Carrier = request.Carrier,
            ServiceLevel = request.ServiceLevel,
            Priority = request.Priority ?? 100,
            OrderType = request.OrderType ?? OrderType.Standard,
            Status = OrderStatus.Draft,
            Notes = request.Notes
        };

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            await _scope.EnsureItemAsync(line.ItemId, ct);

            if (line.OrderedQuantity <= 0)
                throw new BusinessRuleViolationException(
                    $"Line {lineNumber}: ordered quantity must be greater than zero.");

            if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == line.UomId, ct))
                throw new NotFoundException("UnitOfMeasure", line.UomId);

            order.Details.Add(new OrderDetail
            {
                LineNumber = lineNumber++,
                ItemId = line.ItemId,
                OrderedQuantity = line.OrderedQuantity,
                UomId = line.UomId,
                RequiredLotNumber = line.RequiredLotNumber,
                RequiredSerialNumber = line.RequiredSerialNumber,
                MinimumShelfLifeDays = line.MinimumShelfLifeDays,
                Status = OrderDetailStatus.Open
            });
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        // Doc §10 header totals, needed by future order batching.
        await RecalculateTotalsAsync(order.Id, ct);

        return await GetAsync(order.Id, ct);
    }

    /// <summary>Draft -> Created.</summary>
    public async Task<OrderDto> ConfirmAsync(Guid id, CancellationToken ct = default)
        => await TransitionAsync(id, OrderStatus.Created, ct);

    /// <summary>
    /// POST /api/orders/{id}/release (§12). Created -> Released. Only a released order
    /// is a candidate for allocation.
    /// </summary>
    public async Task<OrderDto> ReleaseAsync(Guid id, CancellationToken ct = default)
    {
        var order = await Scoped().Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(OrderMaster), id);

        order.TransitionTo(OrderStatus.Released);
        order.ReleasedAt = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Picked -> Packed.</summary>
    public async Task<OrderDto> PackAsync(Guid id, CancellationToken ct = default)
        => await TransitionAsync(id, OrderStatus.Packed, ct);

    /// <summary>
    /// Cancels an order. Any stock still reserved is released first so it does not stay
    /// stranded (rule §11.2 keeps on-hand untouched throughout).
    /// </summary>
    public async Task<OrderDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var order = await Scoped()
            .Include(o => o.Details).ThenInclude(d => d.Allocations)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(OrderMaster), id);

        if (order.Details.Any(d => d.ShippedQuantity > 0))
            throw new BusinessRuleViolationException(
                "This order has already shipped stock and cannot be cancelled.");

        order.TransitionTo(OrderStatus.Cancelled);

        foreach (var detail in order.Details) detail.Status = OrderDetailStatus.Cancelled;

        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task<OrderDto> TransitionAsync(Guid id, OrderStatus target, CancellationToken ct)
    {
        var order = await Scoped().Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(OrderMaster), id);

        order.TransitionTo(target);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /// <summary>Recomputes the §10 header totals from the current lines.</summary>
    internal async Task RecalculateTotalsAsync(Guid orderId, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.Details).ThenInclude(d => d.Item)
            .FirstAsync(o => o.Id == orderId, ct);

        order.RecalculateTotals();
        await _db.SaveChangesAsync(ct);
    }

    private async Task<string> GenerateOrderNumberAsync(CancellationToken ct)
    {
        var prefix = $"SO-{_clock.UtcNow:yyyyMMdd}-";

        var last = await _db.Orders
            .Where(o => o.AccountId == _scope.AccountId && o.OrderNumber.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(ct);

        var next = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
        return prefix + next.ToString("D4");
    }

    internal static OrderDto ToDto(OrderMaster o) => new(
        o.Id, o.AccountId, o.WarehouseId, o.OrderNumber,
        o.CustomerId, o.Customer?.Name,
        o.OrderDate, o.RequiredShipDate, o.Carrier, o.ServiceLevel, o.Priority,
        o.OrderType, o.Status,
        o.TotalWeight, o.TotalVolume, o.TotalLineCount, o.TotalQuantity,
        o.ReleasedAt, o.ShippedAt, o.Notes, o.CreatedAt,
        o.Details.OrderBy(d => d.LineNumber).Select(d => new OrderDetailDto(
            d.Id, d.LineNumber,
            d.ItemId, d.Item?.Sku ?? string.Empty, d.Item?.Name ?? string.Empty,
            d.OrderedQuantity, d.AllocatedQuantity, d.PickedQuantity, d.ShippedQuantity,
            d.UnallocatedQuantity,
            d.UomId, d.Uom?.Code ?? string.Empty,
            d.RequiredLotNumber, d.RequiredSerialNumber, d.MinimumShelfLifeDays,
            d.Status,
            d.Allocations.Select(a => new AllocationDto(
                a.Id, a.OrderDetailId, a.InventoryBalanceId,
                a.LocationId, a.Location?.Code ?? string.Empty,
                a.ItemId, a.Item?.Sku ?? string.Empty,
                a.LotId, a.Lot?.LotNumber, a.Lot?.ExpirationDate,
                a.SerialId, a.Serial?.Serial,
                a.LicensePlateId,
                a.AllocatedQuantity, a.PickedQuantity,
                a.AllocationStrategy, a.Status)).ToList())).ToList());
}
