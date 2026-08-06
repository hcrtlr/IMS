using IMS.Domain.Entities.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IMS.Infrastructure.Persistence.Configurations;

public class InventoryStatusConfiguration : IEntityTypeConfiguration<InventoryStatus>
{
    public void Configure(EntityTypeBuilder<InventoryStatus> b)
    {
        b.ToTable("inventory_statuses");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();

        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> b)
    {
        b.ToTable("lots");
        b.HasKey(x => x.Id);

        b.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.SupplierLotNumber).HasMaxLength(100);
        b.Property(x => x.Status).HasConversion<int>();

        b.HasOne(x => x.Item)
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Acceptance scenario 4: two lots of the same item stay distinct records.
        b.HasIndex(x => new { x.ItemId, x.LotNumber }).IsUnique();

        // Ordering index for future FEFO allocation.
        b.HasIndex(x => new { x.ItemId, x.ExpirationDate });
    }
}

public class SerialNumberConfiguration : IEntityTypeConfiguration<SerialNumber>
{
    public void Configure(EntityTypeBuilder<SerialNumber> b)
    {
        b.ToTable("serial_numbers");
        b.HasKey(x => x.Id);

        b.Property(x => x.Serial).HasMaxLength(100).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();

        b.HasOne(x => x.Item)
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Lot)
            .WithMany()
            .HasForeignKey(x => x.LotId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.ItemId, x.Serial }).IsUnique();
    }
}

public class LicensePlateConfiguration : IEntityTypeConfiguration<LicensePlate>
{
    public void Configure(EntityTypeBuilder<LicensePlate> b)
    {
        b.ToTable("license_plates");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.Property(x => x.LicensePlateType).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();

        b.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Doc §5.5: nesting is preferred (Pallet -> Case -> ...).
        b.HasOne(x => x.ParentLicensePlate)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentLicensePlateId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CurrentLocation)
            .WithMany()
            .HasForeignKey(x => x.CurrentLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasIndex(x => x.ParentLicensePlateId);
    }
}

public class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> b)
    {
        b.ToTable("inventory_balances");
        b.HasKey(x => x.Id);

        b.Property(x => x.OnHandQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.AllocatedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.HoldQuantity).HasPrecision(18, 4).IsRequired();

        // Doc §5.1 formula, enforced by PostgreSQL rather than application code so the
        // stored value can never drift from OnHand - Allocated - Hold.
        b.Property(x => x.AvailableQuantity)
            .HasPrecision(18, 4)
            .HasComputedColumnSql(
                "\"OnHandQuantity\" - \"AllocatedQuantity\" - \"HoldQuantity\"",
                stored: true);

        // Doc §5.1 / §11.11 - optimistic concurrency token.
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Location).WithMany()
            .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InventoryStatus).WithMany()
            .HasForeignKey(x => x.InventoryStatusId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Lot).WithMany(l => l.Balances)
            .HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Serial).WithMany()
            .HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.LicensePlate).WithMany(lp => lp.Balances)
            .HasForeignKey(x => x.LicensePlateId).OnDelete(DeleteBehavior.Restrict);

        // NOTE: the doc §5.1 uniqueness tuple includes nullable columns. PostgreSQL treats
        // NULLs as distinct, so a plain unique index would allow duplicate rows whenever
        // lot/serial/LPN are null. The unique index is therefore created over COALESCE()
        // sentinels in raw SQL - see the migration's ux_inventory_balances_unique_combo.

        b.HasIndex(x => new { x.WarehouseId, x.ItemId });
        b.HasIndex(x => new { x.WarehouseId, x.LocationId });
        b.HasIndex(x => new { x.ItemId, x.InventoryStatusId });

        // Doc §10: FIFO ordering support.
        b.HasIndex(x => new { x.ItemId, x.ReceivedAt });
    }
}

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
    {
        b.ToTable("inventory_transactions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.TransactionType).HasConversion<int>();
        b.Property(x => x.ReferenceType).HasConversion<int>();
        b.Property(x => x.PerformedBy).HasMaxLength(150);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FromLocation).WithMany()
            .HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ToLocation).WithMany()
            .HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FromInventoryStatus).WithMany()
            .HasForeignKey(x => x.FromInventoryStatusId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ToInventoryStatus).WithMany()
            .HasForeignKey(x => x.ToInventoryStatusId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Lot).WithMany()
            .HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Serial).WithMany()
            .HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.LicensePlate).WithMany()
            .HasForeignKey(x => x.LicensePlateId).OnDelete(DeleteBehavior.Restrict);

        // GET /api/inventory/transactions (§12) and the Faz 5 transaction report.
        b.HasIndex(x => new { x.WarehouseId, x.CreatedAt });
        b.HasIndex(x => new { x.ItemId, x.CreatedAt });
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });

        // Traces every leg of one business operation (§5.6).
        b.HasIndex(x => x.CorrelationId);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_inventory_transactions_quantity_positive", "\"Quantity\" > 0"));
    }
}
