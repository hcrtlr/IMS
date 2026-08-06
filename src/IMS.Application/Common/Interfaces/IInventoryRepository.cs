using IMS.Domain.Entities.Inventory;

namespace IMS.Application.Common.Interfaces;

/// <summary>
/// Identifies one InventoryBalance row by the doc §5.1 uniqueness tuple.
/// </summary>
public sealed record BalanceKey(
    Guid WarehouseId,
    Guid LocationId,
    Guid ItemId,
    Guid InventoryStatusId,
    Guid? LotId = null,
    Guid? SerialId = null,
    Guid? LicensePlateId = null);

/// <summary>
/// Stock access that needs stronger guarantees than plain EF queries give.
///
/// Doc §11.11 requires concurrency control when several operations touch the same stock.
/// Optimistic concurrency alone (InventoryBalance.Version) detects a conflict but only
/// after the fact, so these methods take PostgreSQL row locks (SELECT ... FOR UPDATE)
/// for the duration of the surrounding transaction. Combined, a balance can never be
/// driven negative by interleaved allocations.
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// Loads a balance by its uniqueness tuple and locks the row for update.
    /// Returns null when no such balance exists yet.
    /// </summary>
    Task<InventoryBalance?> GetBalanceForUpdateAsync(BalanceKey key, CancellationToken ct = default);

    /// <summary>
    /// Loads a balance by primary key and locks the row for update.
    /// </summary>
    Task<InventoryBalance?> GetBalanceForUpdateAsync(Guid balanceId, CancellationToken ct = default);

    /// <summary>
    /// Loads the balance for the key, creating an empty one if it does not exist, and
    /// locks it. Used by the destination leg of receipts, putaway and movements.
    /// </summary>
    Task<InventoryBalance> GetOrCreateBalanceForUpdateAsync(
        BalanceKey key,
        DateTimeOffset receivedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Candidate balances that can satisfy an order line, locked for update and returned
    /// in FEFO-then-FIFO order (soonest expiry first, then oldest receipt).
    ///
    /// Only statuses flagged IsAllocatable are returned, which enforces rule §11.4:
    /// hold, damaged and expired stock is never offered to a normal order.
    ///
    /// No allocation ALGORITHM is implemented here (doc §10 defers those); this is a
    /// deterministic ordering so the data groundwork is demonstrably in place.
    /// </summary>
    Task<IReadOnlyList<InventoryBalance>> GetAllocationCandidatesForUpdateAsync(
        Guid warehouseId,
        Guid itemId,
        string? requiredLotNumber,
        string? requiredSerialNumber,
        int? minimumShelfLifeDays,
        DateTimeOffset asOf,
        CancellationToken ct = default);

    /// <summary>Total available quantity of an item across a warehouse, allocatable statuses only.</summary>
    Task<decimal> GetAvailableQuantityAsync(Guid warehouseId, Guid itemId, CancellationToken ct = default);

    /// <summary>Deletes balance rows that carry no quantity at all, keeping the table tidy.</summary>
    Task RemoveEmptyBalanceAsync(InventoryBalance balance, CancellationToken ct = default);
}
