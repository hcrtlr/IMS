using IMS.Application.Common.Models;
using IMS.Domain.Enums;

namespace IMS.Application.Features.Inventory;

// ---------------------------------------------------------------------------
// Doc §5.1 - inventory balance views
// ---------------------------------------------------------------------------

public sealed record InventoryBalanceDto(
    Guid Id,
    Guid WarehouseId,
    Guid LocationId, string LocationCode, string ZoneCode, ZoneType ZoneType,
    Guid ItemId, string Sku, string ItemName,
    Guid InventoryStatusId, string StatusCode, bool IsAllocatable,
    Guid? LotId, string? LotNumber, DateTimeOffset? ExpirationDate,
    Guid? SerialId, string? SerialNumber,
    Guid? LicensePlateId, string? LicensePlateCode,
    decimal OnHandQuantity, decimal AllocatedQuantity, decimal HoldQuantity,
    decimal AvailableQuantity,
    DateTimeOffset ReceivedAt, DateTimeOffset? LastMovementAt, int Version);

/// <summary>Aggregated stock position for one item across a warehouse.</summary>
public sealed record ItemStockSummaryDto(
    Guid ItemId, string Sku, string ItemName, string BaseUomCode,
    decimal TotalOnHand, decimal TotalAllocated, decimal TotalHold, decimal TotalAvailable,
    int LocationCount, int LotCount);

public sealed class InventoryQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? InventoryStatusId { get; set; }
    public Guid? LotId { get; set; }
    public Guid? LicensePlateId { get; set; }

    /// <summary>Hide rows whose quantities are all zero.</summary>
    public bool NonZeroOnly { get; set; } = true;
}

// ---------------------------------------------------------------------------
// Doc §5.6 - transaction ledger views
// ---------------------------------------------------------------------------

public sealed record InventoryTransactionDto(
    Guid Id, Guid WarehouseId,
    Guid ItemId, string Sku,
    Guid? FromLocationId, string? FromLocationCode,
    Guid? ToLocationId, string? ToLocationCode,
    string? FromStatusCode, string? ToStatusCode,
    Guid? LotId, string? LotNumber,
    Guid? SerialId, string? SerialNumber,
    Guid? LicensePlateId, string? LicensePlateCode,
    InventoryTransactionType TransactionType,
    decimal Quantity,
    TransactionReferenceType ReferenceType, Guid? ReferenceId,
    Guid CorrelationId, string? PerformedBy,
    DateTimeOffset CreatedAt, string? Notes);

public sealed class TransactionQuery : PagedQuery
{
    public Guid? WarehouseId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? LotId { get; set; }
    public InventoryTransactionType? TransactionType { get; set; }
    public TransactionReferenceType? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
}

// ---------------------------------------------------------------------------
// Faz 2 - manual stock entry and movement
// ---------------------------------------------------------------------------

/// <summary>
/// Faz 2 "Manuel stok girisi" - books opening stock directly into a location without an
/// inbound order. Writes a Receipt-type transaction referencing Manual.
/// </summary>
public sealed record ManualStockEntryRequest(
    Guid WarehouseId,
    Guid LocationId,
    Guid ItemId,
    decimal Quantity,
    Guid? UomId,
    Guid? InventoryStatusId,
    string? LotNumber,
    DateTimeOffset? ManufactureDate,
    DateTimeOffset? ExpirationDate,
    string? SupplierLotNumber,
    string? SerialNumber,
    Guid? LicensePlateId,
    string? Notes);

/// <summary>Doc §8 - POST /api/inventory/movements.</summary>
public sealed record InventoryMovementRequest(
    Guid WarehouseId,
    Guid ItemId,
    Guid FromLocationId,
    Guid ToLocationId,
    decimal Quantity,
    Guid InventoryStatusId,
    Guid? LotId,
    Guid? SerialId,
    Guid? LicensePlateId,
    string? Notes);

/// <summary>Doc §12 - POST /api/inventory/status-change.</summary>
public sealed record StatusChangeRequest(
    Guid WarehouseId,
    Guid ItemId,
    Guid LocationId,
    Guid FromInventoryStatusId,
    Guid ToInventoryStatusId,
    decimal Quantity,
    Guid? LotId,
    Guid? SerialId,
    Guid? LicensePlateId,
    string? Notes);

/// <summary>Faz 5 - place stock on hold without changing its status.</summary>
public sealed record HoldRequest(Guid InventoryBalanceId, decimal Quantity, string? Notes);

public sealed record InventoryStatusDto(
    Guid Id, string Code, string Name, string? Description,
    bool IsAllocatable, bool IsPhysicalStock, int DisplayOrder, bool IsActive);

// ---------------------------------------------------------------------------
// Doc §5.3 / §5.4 / §5.5 - lot, serial, license plate
// ---------------------------------------------------------------------------

public sealed record LotDto(
    Guid Id, Guid ItemId, string Sku, string LotNumber,
    DateTimeOffset? ManufactureDate, DateTimeOffset? ReceivedDate, DateTimeOffset? ExpirationDate,
    string? SupplierLotNumber, LotStatus Status,
    int? RemainingShelfLifeDays, decimal TotalOnHand);

public sealed record CreateLotRequest(
    Guid ItemId, string LotNumber,
    DateTimeOffset? ManufactureDate, DateTimeOffset? ExpirationDate, string? SupplierLotNumber);

public sealed record SerialNumberDto(
    Guid Id, Guid ItemId, string Sku, string Serial,
    Guid? LotId, string? LotNumber, SerialStatus Status);

public sealed record CreateSerialRequest(Guid ItemId, string Serial, Guid? LotId);

public sealed record LicensePlateDto(
    Guid Id, Guid WarehouseId, string Code,
    Guid? ParentLicensePlateId, string? ParentCode,
    LicensePlateType LicensePlateType,
    Guid? CurrentLocationId, string? CurrentLocationCode,
    LicensePlateStatus Status,
    int ChildCount, decimal TotalOnHand);

public sealed record CreateLicensePlateRequest(
    Guid WarehouseId, string Code, LicensePlateType LicensePlateType,
    Guid? ParentLicensePlateId, Guid? CurrentLocationId);

/// <summary>Doc §5.5 - nested LPN view (Pallet -> Case 1 / Case 2 / ...).</summary>
public sealed record LicensePlateTreeDto(
    Guid Id, string Code, LicensePlateType LicensePlateType,
    LicensePlateStatus Status, string? CurrentLocationCode,
    decimal TotalOnHand,
    IReadOnlyList<LicensePlateTreeDto> Children);
