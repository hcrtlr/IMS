using IMS.Application.Common.Interfaces;
using IMS.Application.Common.Models;
using IMS.Application.Common.Services;
using IMS.Domain.Entities.Inbound;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.Inbound;

/// <summary>
/// Doc §6.1 / §6.2 - inbound order header and lines.
/// Lifecycle: Draft -> Expected -> PartiallyReceived -> Received -> Completed -> Cancelled.
/// </summary>
public class InboundOrderService
{
    private readonly IApplicationDbContext _db;
    private readonly ScopeGuard _scope;
    private readonly IDateTimeProvider _clock;

    public InboundOrderService(IApplicationDbContext db, ScopeGuard scope, IDateTimeProvider clock)
    {
        _db = db;
        _scope = scope;
        _clock = clock;
    }

    private IQueryable<InboundOrder> Scoped()
        => _db.InboundOrders.Where(o => o.AccountId == _scope.AccountId);

    public async Task<PagedResult<InboundOrderSummaryDto>> ListAsync(
        InboundOrderQuery query, CancellationToken ct = default)
    {
        var q = Scoped();

        if (query.WarehouseId.HasValue) q = q.Where(o => o.WarehouseId == query.WarehouseId.Value);
        if (query.SupplierId.HasValue) q = q.Where(o => o.SupplierId == query.SupplierId.Value);
        if (query.Status.HasValue) q = q.Where(o => o.Status == query.Status.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            q = q.Where(o => o.OrderNumber.ToLower().Contains(term));
        }

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(o => o.CreatedAt)
            .Skip(query.Skip).Take(query.PageSize)
            .Select(o => new InboundOrderSummaryDto(
                o.Id, o.OrderNumber, o.WarehouseId,
                o.Supplier != null ? o.Supplier.Name : null,
                o.ExpectedArrivalDate, o.Status,
                o.Details.Count,
                o.Details.Sum(d => (decimal?)d.ExpectedQuantity) ?? 0m,
                o.Details.Sum(d => (decimal?)d.ReceivedQuantity) ?? 0m))
            .ToListAsync(ct);

        return new PagedResult<InboundOrderSummaryDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<InboundOrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var order = await Scoped()
            .Include(o => o.Warehouse)
            .Include(o => o.Supplier)
            .Include(o => o.Details).ThenInclude(d => d.Item)
            .Include(o => o.Details).ThenInclude(d => d.Uom)
            .Include(o => o.Receipts).ThenInclude(r => r.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(InboundOrder), id);

        return ToDto(order);
    }

    public async Task<InboundOrderDto> CreateAsync(
        CreateInboundOrderRequest request, CancellationToken ct = default)
    {
        await _scope.EnsureWarehouseAsync(request.WarehouseId, ct);

        if (request.Lines is null || request.Lines.Count == 0)
            throw new BusinessRuleViolationException("An inbound order must have at least one line.");

        if (request.SupplierId.HasValue)
        {
            var supplierOk = await _db.Suppliers
                .AnyAsync(s => s.Id == request.SupplierId.Value && s.AccountId == _scope.AccountId, ct);

            if (!supplierOk) throw new NotFoundException("Supplier", request.SupplierId.Value);
        }

        var orderNumber = string.IsNullOrWhiteSpace(request.OrderNumber)
            ? await GenerateOrderNumberAsync(ct)
            : request.OrderNumber;

        if (await _db.InboundOrders.AnyAsync(
                o => o.AccountId == _scope.AccountId && o.OrderNumber == orderNumber, ct))
            throw new DuplicateEntityException($"Inbound order '{orderNumber}' already exists.");

        var order = new InboundOrder
        {
            AccountId = _scope.AccountId,
            WarehouseId = request.WarehouseId,
            OrderNumber = orderNumber,
            SupplierId = request.SupplierId,
            ExpectedArrivalDate = request.ExpectedArrivalDate,
            Status = InboundOrderStatus.Draft,
            Notes = request.Notes
        };

        var lineNumber = 1;
        foreach (var line in request.Lines)
        {
            await _scope.EnsureItemAsync(line.ItemId, ct);

            if (line.ExpectedQuantity <= 0)
                throw new BusinessRuleViolationException(
                    $"Line {lineNumber}: expected quantity must be greater than zero.");

            if (!await _db.UnitsOfMeasure.AnyAsync(u => u.Id == line.UomId, ct))
                throw new NotFoundException("UnitOfMeasure", line.UomId);

            order.Details.Add(new InboundOrderDetail
            {
                LineNumber = lineNumber++,
                ItemId = line.ItemId,
                ExpectedQuantity = line.ExpectedQuantity,
                ReceivedQuantity = 0m,
                UomId = line.UomId,
                ExpectedLotNumber = line.ExpectedLotNumber,
                ExpectedExpirationDate = line.ExpectedExpirationDate
            });
        }

        _db.InboundOrders.Add(order);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(order.Id, ct);
    }

    /// <summary>
    /// Draft -> Expected. Until an order is confirmed as expected it cannot be received
    /// against, which keeps half-entered orders out of the receiving screen.
    /// </summary>
    public async Task<InboundOrderDto> ConfirmAsync(Guid id, CancellationToken ct = default)
    {
        var order = await Scoped().Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(InboundOrder), id);

        order.TransitionTo(InboundOrderStatus.Expected);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Received -> Completed. Closing an order stops further receipts against it.
    /// </summary>
    public async Task<InboundOrderDto> CompleteAsync(Guid id, CancellationToken ct = default)
    {
        var order = await Scoped().Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(InboundOrder), id);

        order.TransitionTo(InboundOrderStatus.Completed);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task<InboundOrderDto> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var order = await Scoped().Include(o => o.Details)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(nameof(InboundOrder), id);

        // Stock already received cannot be un-received by cancelling the paperwork;
        // that would need an adjustment, which is auditable.
        if (order.Details.Any(d => d.ReceivedQuantity > 0))
            throw new BusinessRuleViolationException(
                "This order already has received stock and cannot be cancelled. " +
                "Reverse the stock with an inventory adjustment instead.");

        order.TransitionTo(InboundOrderStatus.Cancelled);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(id, ct);
    }

    /// <summary>Sequential, human-readable order number: IB-yyyyMMdd-0001.</summary>
    private async Task<string> GenerateOrderNumberAsync(CancellationToken ct)
    {
        var prefix = $"IB-{_clock.UtcNow:yyyyMMdd}-";

        var last = await _db.InboundOrders
            .Where(o => o.AccountId == _scope.AccountId && o.OrderNumber.StartsWith(prefix))
            .OrderByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync(ct);

        var next = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
        return prefix + next.ToString("D4");
    }

    internal static InboundOrderDto ToDto(InboundOrder o) => new(
        o.Id, o.WarehouseId, o.Warehouse?.Code ?? string.Empty,
        o.OrderNumber,
        o.SupplierId, o.Supplier?.Name,
        o.ExpectedArrivalDate, o.Status, o.Notes, o.CreatedAt,
        o.Details.OrderBy(d => d.LineNumber).Select(d => new InboundOrderDetailDto(
            d.Id, d.LineNumber,
            d.ItemId, d.Item?.Sku ?? string.Empty, d.Item?.Name ?? string.Empty,
            d.ExpectedQuantity, d.ReceivedQuantity, d.OutstandingQuantity,
            d.UomId, d.Uom?.Code ?? string.Empty,
            d.ExpectedLotNumber, d.ExpectedExpirationDate,
            d.IsFullyReceived)).ToList(),
        o.Receipts.OrderBy(r => r.ReceivedAt).Select(r => new ReceiptSummaryDto(
            r.Id, r.ReceiptNumber, r.ReceivedAt, r.ReceivedBy,
            r.Lines.Count, r.Lines.Sum(l => l.BaseQuantity))).ToList());
}
