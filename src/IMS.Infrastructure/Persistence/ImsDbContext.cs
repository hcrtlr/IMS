using System.Reflection;
using IMS.Application.Common.Interfaces;
using IMS.Domain.Common;
using IMS.Domain.Entities.Algorithms;
using IMS.Domain.Entities.Counting;
using IMS.Domain.Entities.Inbound;
using IMS.Domain.Entities.Inventory;
using IMS.Domain.Entities.MasterData;
using IMS.Domain.Entities.Outbound;
using IMS.Domain.Entities.Security;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IMS.Infrastructure.Persistence;

/// <summary>
/// EF Core / PostgreSQL implementation of <see cref="IApplicationDbContext"/>.
///
/// Beyond mapping, this type enforces two doc rules centrally so no individual service
/// can forget them:
///   §11.10 - InventoryTransaction rows are never updated or deleted.
///   §5.1   - InventoryBalance.Version is bumped on every change (optimistic concurrency).
/// </summary>
public class ImsDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUser? _currentUser;
    private readonly IDateTimeProvider? _clock;

    public ImsDbContext(DbContextOptions<ImsDbContext> options) : base(options) { }

    public ImsDbContext(
        DbContextOptions<ImsDbContext> options,
        ICurrentUser currentUser,
        IDateTimeProvider clock) : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    // Faz 1
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<LocationProfile> LocationProfiles => Set<LocationProfile>();
    public DbSet<ItemMaster> Items => Set<ItemMaster>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<AttributeDefinition> AttributeDefinitions => Set<AttributeDefinition>();
    public DbSet<ItemAttributeValue> ItemAttributeValues => Set<ItemAttributeValue>();
    public DbSet<ItemUom> ItemUoms => Set<ItemUom>();
    public DbSet<ItemBarcode> ItemBarcodes => Set<ItemBarcode>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();

    // Faz 2
    public DbSet<InventoryStatus> InventoryStatuses => Set<InventoryStatus>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<SerialNumber> SerialNumbers => Set<SerialNumber>();
    public DbSet<LicensePlate> LicensePlates => Set<LicensePlate>();

    // Faz 3
    public DbSet<InboundOrder> InboundOrders => Set<InboundOrder>();
    public DbSet<InboundOrderDetail> InboundOrderDetails => Set<InboundOrderDetail>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptLine> ReceiptLines => Set<ReceiptLine>();
    public DbSet<PutawayTask> PutawayTasks => Set<PutawayTask>();

    // Faz 4
    public DbSet<OrderMaster> Orders => Set<OrderMaster>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<InventoryAllocation> InventoryAllocations => Set<InventoryAllocation>();
    public DbSet<PickTask> PickTasks => Set<PickTask>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();

    // Faz 5
    public DbSet<CountPlan> CountPlans => Set<CountPlan>();
    public DbSet<CountTask> CountTasks => Set<CountTask>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();

    // Faz 6
    public DbSet<AlgorithmConfiguration> AlgorithmConfigurations => Set<AlgorithmConfiguration>();
    public DbSet<SlottingRecommendation> SlottingRecommendations => Set<SlottingRecommendation>();
    public DbSet<PickingPlan> PickingPlans => Set<PickingPlan>();
    public DbSet<PickingRouteStop> PickingRouteStops => Set<PickingRouteStop>();
    public DbSet<OrderHistorySnapshot> OrderHistory => Set<OrderHistorySnapshot>();

    // Security
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Quantities carry 4 decimal places throughout; money is not modelled in this system.
        builder.Properties<decimal>().HavePrecision(18, 4);
        builder.Properties<string>().HaveMaxLength(256);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardTransactionImmutability();
        StampAuditFields();
        BumpBalanceVersions();

        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        GuardTransactionImmutability();
        StampAuditFields();
        BumpBalanceVersions();

        return base.SaveChanges();
    }

    /// <summary>
    /// Doc §11.12 - stock balances and their transaction records must roll back together.
    /// Nested calls join the ambient transaction rather than opening a second one.
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        var strategy = Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using IDbContextTransaction transaction =
                await Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

            try
            {
                var result = await operation(ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Doc §11.10 - "Inventory Transaction kayitlari silinmemelidir." Also blocks updates,
    /// since §5.6 calls the table immutable. The database has triggers as a second line
    /// of defence for writes that bypass EF entirely.
    /// </summary>
    private void GuardTransactionImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<InventoryTransaction>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new BusinessRuleViolationException(
                    "InventoryTransaction records are immutable and must never be updated or deleted.",
                    ruleNumber: 10);
            }
        }
    }

    private void StampAuditFields()
    {
        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;
        var user = _currentUser?.Username;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = user;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = user;
                    // Never let a create stamp be overwritten by a later update.
                    entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<InventoryTransaction>()
                     .Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
            entry.Entity.PerformedBy ??= user;
        }
    }

    /// <summary>
    /// Doc §5.1 - Version is the optimistic concurrency token. Incrementing it here means
    /// a concurrent writer that read the old value fails its UPDATE ... WHERE Version = n.
    /// </summary>
    private void BumpBalanceVersions()
    {
        var now = _clock?.UtcNow ?? DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<InventoryBalance>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.Version = 1;
                    if (entry.Entity.ReceivedAt == default) entry.Entity.ReceivedAt = now;
                    entry.Entity.LastMovementAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.Version++;
                    entry.Entity.LastMovementAt = now;
                    break;
            }
        }
    }
}
