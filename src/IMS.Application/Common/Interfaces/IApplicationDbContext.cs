using IMS.Domain.Entities.Algorithms;
using IMS.Domain.Entities.Counting;
using IMS.Domain.Entities.Inbound;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Entities.Outbound;
using IMS.Domain.Entities.Security;
using Microsoft.EntityFrameworkCore;

namespace IMS.Application.Common.Interfaces;

/// <summary>
/// The persistence contract the application layer works against. The concrete EF Core /
/// PostgreSQL implementation lives in Infrastructure, which keeps business rules free of
/// provider details while still allowing composable queries.
/// </summary>
public interface IApplicationDbContext
{
    // --- Faz 1: master data ---
    DbSet<Account> Accounts { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Zone> Zones { get; }
    DbSet<Location> Locations { get; }
    DbSet<LocationProfile> LocationProfiles { get; }
    DbSet<ItemMaster> Items { get; }
    DbSet<ItemCategory> ItemCategories { get; }
    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }
    DbSet<AttributeDefinition> AttributeDefinitions { get; }
    DbSet<ItemAttributeValue> ItemAttributeValues { get; }
    DbSet<ItemUom> ItemUoms { get; }
    DbSet<ItemBarcode> ItemBarcodes { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Customer> Customers { get; }

    // --- Faz 2: inventory core ---
    DbSet<InventoryStatus> InventoryStatuses { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<InventoryTransaction> InventoryTransactions { get; }
    DbSet<Lot> Lots { get; }
    DbSet<SerialNumber> SerialNumbers { get; }
    DbSet<LicensePlate> LicensePlates { get; }

    // --- Faz 3: inbound ---
    DbSet<InboundOrder> InboundOrders { get; }
    DbSet<InboundOrderDetail> InboundOrderDetails { get; }
    DbSet<Receipt> Receipts { get; }
    DbSet<ReceiptLine> ReceiptLines { get; }
    DbSet<PutawayTask> PutawayTasks { get; }

    // --- Faz 4: outbound ---
    DbSet<OrderMaster> Orders { get; }
    DbSet<OrderDetail> OrderDetails { get; }
    DbSet<InventoryAllocation> InventoryAllocations { get; }
    DbSet<PickTask> PickTasks { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<ShipmentLine> ShipmentLines { get; }

    // --- Faz 5: inventory control ---
    DbSet<CountPlan> CountPlans { get; }
    DbSet<CountTask> CountTasks { get; }
    DbSet<InventoryAdjustment> InventoryAdjustments { get; }

    // --- Faz 6: algorithm readiness ---
    DbSet<AlgorithmConfiguration> AlgorithmConfigurations { get; }
    DbSet<SlottingRecommendation> SlottingRecommendations { get; }
    DbSet<PickingPlan> PickingPlans { get; }
    DbSet<PickingRouteStop> PickingRouteStops { get; }
    DbSet<OrderHistorySnapshot> OrderHistory { get; }

    // --- Security (not in the source doc; see docs/ASSUMPTIONS.md) ---
    DbSet<ApplicationUser> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a single database transaction.
    /// Doc §11.12 - if a business operation fails, stock balances and their transaction
    /// records roll back together.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
