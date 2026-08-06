using IMS.Application.Common.Interfaces;
using IMS.Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace IMS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Stock access with PostgreSQL row-level locking.
///
/// Doc §11.11 requires concurrency control on simultaneous stock operations. EF's
/// optimistic token (InventoryBalance.Version) detects a conflict only at save time,
/// which for allocation means two callers can both read "10 available" and both decide
/// they may take 8. Taking SELECT ... FOR UPDATE serialises them at read time instead,
/// so the second caller sees the first one's result and correctly reports a shortfall.
///
/// The locks live for the enclosing transaction, which the caller opens via
/// IApplicationDbContext.ExecuteInTransactionAsync.
/// </summary>
public class InventoryRepository : IInventoryRepository
{
    private readonly ImsDbContext _db;

    public InventoryRepository(ImsDbContext db) => _db = db;

    /// <summary>
    /// Sentinel used for null lot/serial/LPN. PostgreSQL treats NULL as distinct in both
    /// unique indexes and equality, so the doc §5.1 tuple is compared over COALESCE()
    /// with this value - the same expression the unique index is built on.
    /// </summary>
    private static readonly Guid NullKey = Guid.Empty;

    public async Task<InventoryBalance?> GetBalanceForUpdateAsync(BalanceKey key, CancellationToken ct = default)
    {
        const string sql = """
            SELECT * FROM inventory_balances
            WHERE "WarehouseId" = @wh
              AND "LocationId" = @loc
              AND "ItemId" = @item
              AND "InventoryStatusId" = @status
              AND COALESCE("LotId", @nullKey) = COALESCE(@lot, @nullKey)
              AND COALESCE("SerialId", @nullKey) = COALESCE(@serial, @nullKey)
              AND COALESCE("LicensePlateId", @nullKey) = COALESCE(@lpn, @nullKey)
            FOR UPDATE
            """;

        // Every parameter is explicitly typed so an untyped NULL can never leave
        // PostgreSQL unable to determine the parameter's type.
        var rows = await _db.InventoryBalances
            .FromSqlRaw(sql,
                new NpgsqlParameter("wh", NpgsqlDbType.Uuid) { Value = key.WarehouseId },
                new NpgsqlParameter("loc", NpgsqlDbType.Uuid) { Value = key.LocationId },
                new NpgsqlParameter("item", NpgsqlDbType.Uuid) { Value = key.ItemId },
                new NpgsqlParameter("status", NpgsqlDbType.Uuid) { Value = key.InventoryStatusId },
                new NpgsqlParameter("lot", NpgsqlDbType.Uuid)
                    { Value = (object?)key.LotId ?? DBNull.Value },
                new NpgsqlParameter("serial", NpgsqlDbType.Uuid)
                    { Value = (object?)key.SerialId ?? DBNull.Value },
                new NpgsqlParameter("lpn", NpgsqlDbType.Uuid)
                    { Value = (object?)key.LicensePlateId ?? DBNull.Value },
                new NpgsqlParameter("nullKey", NpgsqlDbType.Uuid) { Value = NullKey })
            .ToListAsync(ct);

        return rows.FirstOrDefault();
    }

    public async Task<InventoryBalance?> GetBalanceForUpdateAsync(Guid balanceId, CancellationToken ct = default)
    {
        var rows = await _db.InventoryBalances
            .FromSqlRaw("SELECT * FROM inventory_balances WHERE \"Id\" = @id FOR UPDATE",
                new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = balanceId })
            .ToListAsync(ct);

        return rows.FirstOrDefault();
    }

    public async Task<InventoryBalance> GetOrCreateBalanceForUpdateAsync(
        BalanceKey key,
        DateTimeOffset receivedAt,
        CancellationToken ct = default)
    {
        var existing = await GetBalanceForUpdateAsync(key, ct);
        if (existing is not null) return existing;

        var balance = new InventoryBalance
        {
            WarehouseId = key.WarehouseId,
            LocationId = key.LocationId,
            ItemId = key.ItemId,
            InventoryStatusId = key.InventoryStatusId,
            LotId = key.LotId,
            SerialId = key.SerialId,
            LicensePlateId = key.LicensePlateId,
            OnHandQuantity = 0m,
            AllocatedQuantity = 0m,
            HoldQuantity = 0m,
            ReceivedAt = receivedAt
        };

        _db.InventoryBalances.Add(balance);

        // Flush immediately so the row exists (and is lockable) for the rest of the
        // transaction, and so a concurrent creator collides on the unique index now
        // rather than at the end of the operation.
        await _db.SaveChangesAsync(ct);

        return balance;
    }

    public async Task<IReadOnlyList<InventoryBalance>> GetAllocationCandidatesForUpdateAsync(
        Guid warehouseId,
        Guid itemId,
        string? requiredLotNumber,
        string? requiredSerialNumber,
        int? minimumShelfLifeDays,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        // Rule §11.4 is enforced by joining only statuses flagged IsAllocatable, so
        // QualityHold / Damaged / Expired / Quarantine / Blocked stock is never a candidate.
        //
        // Ordering is FEFO-then-FIFO: soonest expiry first, then oldest receipt. This is
        // deterministic source selection, not a slotting algorithm - doc §10 defers those.
        const string sql = """
            SELECT b.* FROM inventory_balances b
            INNER JOIN inventory_statuses s ON s."Id" = b."InventoryStatusId"
            LEFT JOIN lots l ON l."Id" = b."LotId"
            LEFT JOIN serial_numbers sn ON sn."Id" = b."SerialId"
            WHERE b."WarehouseId" = @wh
              AND b."ItemId" = @item
              AND s."IsAllocatable" = TRUE
              AND (b."OnHandQuantity" - b."AllocatedQuantity" - b."HoldQuantity") > 0
              AND (@lotNumber IS NULL OR l."LotNumber" = @lotNumber)
              AND (@serialNumber IS NULL OR sn."Serial" = @serialNumber)
              AND (l."ExpirationDate" IS NULL OR l."ExpirationDate" > @asOf)
              AND (@minShelfLife IS NULL
                   OR l."ExpirationDate" IS NULL
                   OR l."ExpirationDate" >= @asOf + make_interval(days => @minShelfLife))
            ORDER BY l."ExpirationDate" ASC NULLS LAST, b."ReceivedAt" ASC
            FOR UPDATE OF b
            """;

        // Nullable parameters carry an explicit type. PostgreSQL cannot infer the type of
        // an untyped NULL used in "@p IS NULL OR col = @p", and fails with
        // "42P08: could not determine data type of parameter".
        return await _db.InventoryBalances
            .FromSqlRaw(sql,
                new NpgsqlParameter("wh", NpgsqlDbType.Uuid) { Value = warehouseId },
                new NpgsqlParameter("item", NpgsqlDbType.Uuid) { Value = itemId },
                new NpgsqlParameter("lotNumber", NpgsqlDbType.Text)
                    { Value = (object?)requiredLotNumber ?? DBNull.Value },
                new NpgsqlParameter("serialNumber", NpgsqlDbType.Text)
                    { Value = (object?)requiredSerialNumber ?? DBNull.Value },
                new NpgsqlParameter("asOf", NpgsqlDbType.TimestampTz) { Value = asOf },
                new NpgsqlParameter("minShelfLife", NpgsqlDbType.Integer)
                    { Value = (object?)minimumShelfLifeDays ?? DBNull.Value })
            .ToListAsync(ct);
    }

    public async Task<decimal> GetAvailableQuantityAsync(
        Guid warehouseId, Guid itemId, CancellationToken ct = default)
    {
        return await _db.InventoryBalances
            .Where(b => b.WarehouseId == warehouseId
                        && b.ItemId == itemId
                        && b.InventoryStatus.IsAllocatable)
            .SumAsync(b => (decimal?)b.AvailableQuantity, ct) ?? 0m;
    }

    public async Task RemoveEmptyBalanceAsync(InventoryBalance balance, CancellationToken ct = default)
    {
        if (!balance.IsEmpty()) return;

        // A drained balance is only prunable if nothing still points at it. Allocations,
        // count tasks and adjustments keep a required reference to the exact balance row
        // they acted on, and those records outlive the stock: a shipped allocation is
        // history, not something to cascade away. Deleting the row here would sever a
        // required relationship and abort the whole shipment.
        var referenced =
            await _db.InventoryAllocations.AnyAsync(a => a.InventoryBalanceId == balance.Id, ct)
            || await _db.CountTasks.AnyAsync(t => t.InventoryBalanceId == balance.Id, ct)
            || await _db.InventoryAdjustments.AnyAsync(a => a.InventoryBalanceId == balance.Id, ct);

        if (referenced) return;

        _db.InventoryBalances.Remove(balance);
    }
}
