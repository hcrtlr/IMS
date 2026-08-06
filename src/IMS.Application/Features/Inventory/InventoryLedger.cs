using IMS.Application.Common.Interfaces;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Features.Inventory;

/// <summary>
/// Describes one stock mutation. Every field the doc §5.6 ledger records is captured
/// here so a transaction can always be written alongside the balance change.
/// </summary>
public sealed record StockMutation
{
    public required Guid WarehouseId { get; init; }
    public required Guid ItemId { get; init; }
    public required decimal Quantity { get; init; }
    public required InventoryTransactionType TransactionType { get; init; }
    public required TransactionReferenceType ReferenceType { get; init; }

    public Guid? ReferenceId { get; init; }
    public Guid? CorrelationId { get; init; }

    public Guid? FromLocationId { get; init; }
    public Guid? ToLocationId { get; init; }
    public Guid? FromInventoryStatusId { get; init; }
    public Guid? ToInventoryStatusId { get; init; }

    public Guid? LotId { get; init; }
    public Guid? SerialId { get; init; }
    public Guid? LicensePlateId { get; init; }

    public string? Notes { get; init; }
}

/// <summary>
/// The single gateway through which stock is allowed to change.
///
/// Doc rule §11.9 ("every stock change must create an inventory transaction") is only
/// trustworthy if there is exactly one code path that can move stock. Inbound, outbound,
/// movement, status change and counting all mutate balances through this service, which
/// writes the matching ledger row in the same unit of work.
///
/// Callers are responsible for opening the surrounding transaction via
/// IApplicationDbContext.ExecuteInTransactionAsync, so rule §11.12 (balance and ledger
/// roll back together) holds across multi-step operations.
/// </summary>
public class InventoryLedger
{
    private readonly IApplicationDbContext _db;
    private readonly IInventoryRepository _inventory;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public InventoryLedger(
        IApplicationDbContext db,
        IInventoryRepository inventory,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _inventory = inventory;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>
    /// Adds stock into a location. Used by receiving, the inbound leg of a movement,
    /// and positive adjustments.
    /// </summary>
    public async Task<InventoryBalance> AddAsync(StockMutation mutation, CancellationToken ct = default)
    {
        if (mutation.ToLocationId is null || mutation.ToInventoryStatusId is null)
            throw new BusinessRuleViolationException(
                "A stock increase requires both a destination location and a destination status.");

        await ValidateTrackingAsync(mutation, ct);

        var key = new BalanceKey(
            mutation.WarehouseId, mutation.ToLocationId.Value, mutation.ItemId,
            mutation.ToInventoryStatusId.Value,
            mutation.LotId, mutation.SerialId, mutation.LicensePlateId);

        var balance = await _inventory.GetOrCreateBalanceForUpdateAsync(key, _clock.UtcNow, ct);

        balance.AddStock(mutation.Quantity);

        await EnforceSerialSingleUnitAsync(balance, ct);

        WriteTransaction(mutation);

        return balance;
    }

    /// <summary>
    /// Removes stock from a location. Refuses to consume quantity that is allocated or
    /// on hold, and can never drive the balance negative.
    /// </summary>
    public async Task<InventoryBalance> RemoveAsync(StockMutation mutation, CancellationToken ct = default)
    {
        if (mutation.FromLocationId is null || mutation.FromInventoryStatusId is null)
            throw new BusinessRuleViolationException(
                "A stock decrease requires both a source location and a source status.");

        var key = new BalanceKey(
            mutation.WarehouseId, mutation.FromLocationId.Value, mutation.ItemId,
            mutation.FromInventoryStatusId.Value,
            mutation.LotId, mutation.SerialId, mutation.LicensePlateId);

        var balance = await _inventory.GetBalanceForUpdateAsync(key, ct)
            ?? throw new InsufficientStockException(
                new StockShortfall(mutation.ItemId, await SkuAsync(mutation.ItemId, ct),
                    mutation.Quantity, 0m));

        balance.RemoveStock(mutation.Quantity, await SkuAsync(mutation.ItemId, ct));

        WriteTransaction(mutation);

        await _inventory.RemoveEmptyBalanceAsync(balance, ct);

        return balance;
    }

    /// <summary>
    /// Doc §8 - moves stock between locations inside one transaction: decrement the
    /// source, increment (or create) the destination, and write the ledger entry.
    /// A movement is a single logical event, so it writes ONE transaction carrying both
    /// the From and To location, not two half-entries.
    /// </summary>
    public async Task<(InventoryBalance From, InventoryBalance To)> MoveAsync(
        StockMutation mutation, CancellationToken ct = default)
    {
        if (mutation.FromLocationId is null || mutation.ToLocationId is null)
            throw new BusinessRuleViolationException(
                "A movement requires both a source and a destination location.");

        var fromStatus = mutation.FromInventoryStatusId
            ?? throw new BusinessRuleViolationException("A movement requires a source status.");

        // A pure movement keeps the status; a status change supplies a different target.
        var toStatus = mutation.ToInventoryStatusId ?? fromStatus;

        if (mutation.FromLocationId == mutation.ToLocationId && fromStatus == toStatus)
            throw new BusinessRuleViolationException(
                "Source and destination are identical; nothing would change.");

        var sku = await SkuAsync(mutation.ItemId, ct);

        // Lock the source first, then the destination, always in that order, so two
        // concurrent movements between the same pair cannot deadlock.
        var fromKey = new BalanceKey(
            mutation.WarehouseId, mutation.FromLocationId.Value, mutation.ItemId, fromStatus,
            mutation.LotId, mutation.SerialId, mutation.LicensePlateId);

        var from = await _inventory.GetBalanceForUpdateAsync(fromKey, ct)
            ?? throw new InsufficientStockException(
                new StockShortfall(mutation.ItemId, sku, mutation.Quantity, 0m));

        // Doc §8: "Yetersiz stok varsa islem yapilmamalidir."
        from.RemoveStock(mutation.Quantity, sku);

        var toKey = new BalanceKey(
            mutation.WarehouseId, mutation.ToLocationId.Value, mutation.ItemId, toStatus,
            mutation.LotId, mutation.SerialId, mutation.LicensePlateId);

        var to = await _inventory.GetOrCreateBalanceForUpdateAsync(toKey, from.ReceivedAt, ct);
        to.AddStock(mutation.Quantity);

        await EnforceSerialSingleUnitAsync(to, ct);

        WriteTransaction(mutation with { ToInventoryStatusId = toStatus });

        await _inventory.RemoveEmptyBalanceAsync(from, ct);

        return (from, to);
    }

    /// <summary>
    /// Writes a ledger row for a change that does not itself move quantity between
    /// balances - allocation, deallocation and hold transitions. The caller has already
    /// adjusted the reserved/held columns on the locked balance.
    /// </summary>
    public void RecordNonPhysical(StockMutation mutation) => WriteTransaction(mutation);

    /// <summary>
    /// Doc §11.5 - a serial-tracked balance may never exceed one unit. Enforced here in
    /// addition to the database check constraint so the caller gets a domain error rather
    /// than a raw constraint violation.
    /// </summary>
    private async Task EnforceSerialSingleUnitAsync(InventoryBalance balance, CancellationToken ct)
    {
        if (balance.SerialId is null) return;

        if (balance.OnHandQuantity > 1m)
            throw new BusinessRuleViolationException(
                $"Serial-tracked stock cannot exceed one unit per serial number " +
                $"(attempted {balance.OnHandQuantity}).", ruleNumber: 5);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Enforces that lot / serial / expiration data matches what the item master declares
    /// (doc §11.5 and §11.6).
    /// </summary>
    private async Task ValidateTrackingAsync(StockMutation mutation, CancellationToken ct)
    {
        var item = await _db.Items
            .Where(i => i.Id == mutation.ItemId)
            .Select(i => new
            {
                i.Sku, i.IsLotTracked, i.IsSerialTracked, i.IsExpirationTracked
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Item", mutation.ItemId);

        if (item.IsLotTracked && mutation.LotId is null)
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is lot tracked; a lot is required for this stock movement.");

        if (item.IsSerialTracked && mutation.SerialId is null)
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is serial tracked; a serial number is required for this stock movement.",
                ruleNumber: 5);

        if (!item.IsLotTracked && mutation.LotId is not null)
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is not lot tracked; a lot must not be supplied.");

        if (!item.IsSerialTracked && mutation.SerialId is not null)
            throw new BusinessRuleViolationException(
                $"Item '{item.Sku}' is not serial tracked; a serial number must not be supplied.");

        // Doc §11.6 - expiration-tracked stock must carry an expiration date on its lot.
        if (item.IsExpirationTracked && mutation.LotId is not null)
        {
            var hasExpiry = await _db.Lots
                .AnyAsync(l => l.Id == mutation.LotId && l.ExpirationDate != null, ct);

            if (!hasExpiry)
                throw new BusinessRuleViolationException(
                    $"Item '{item.Sku}' is expiration tracked; lot must carry an expiration date.",
                    ruleNumber: 6);
        }
    }

    private void WriteTransaction(StockMutation mutation)
    {
        if (mutation.Quantity <= 0)
            throw new BusinessRuleViolationException("Transaction quantity must be greater than zero.");

        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            WarehouseId = mutation.WarehouseId,
            ItemId = mutation.ItemId,
            FromLocationId = mutation.FromLocationId,
            ToLocationId = mutation.ToLocationId,
            FromInventoryStatusId = mutation.FromInventoryStatusId,
            ToInventoryStatusId = mutation.ToInventoryStatusId,
            LotId = mutation.LotId,
            SerialId = mutation.SerialId,
            LicensePlateId = mutation.LicensePlateId,
            TransactionType = mutation.TransactionType,
            Quantity = mutation.Quantity,
            ReferenceType = mutation.ReferenceType,
            ReferenceId = mutation.ReferenceId,
            CorrelationId = mutation.CorrelationId ?? Guid.NewGuid(),
            PerformedBy = _currentUser.Username,
            CreatedAt = _clock.UtcNow,
            Notes = mutation.Notes
        });
    }

    private async Task<string> SkuAsync(Guid itemId, CancellationToken ct)
        => await _db.Items.Where(i => i.Id == itemId).Select(i => i.Sku).FirstOrDefaultAsync(ct)
           ?? itemId.ToString();
}
