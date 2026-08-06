using IMS.Domain.Entities.Counting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IMS.Infrastructure.Persistence.Configurations;

public class CountPlanConfiguration : IEntityTypeConfiguration<CountPlan>
{
    public void Configure(EntityTypeBuilder<CountPlan> b)
    {
        b.ToTable("count_plans");
        b.HasKey(x => x.Id);

        b.Property(x => x.PlanNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.CountType).HasConversion<int>();
        b.Property(x => x.SelectionMode).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Zone).WithMany()
            .HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.AccountId, x.PlanNumber }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status });
    }
}

public class CountTaskConfiguration : IEntityTypeConfiguration<CountTask>
{
    public void Configure(EntityTypeBuilder<CountTask> b)
    {
        b.ToTable("count_tasks");
        b.HasKey(x => x.Id);

        b.Property(x => x.SystemQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.CountedQuantity).HasPrecision(18, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.AssignedTo).HasMaxLength(150);
        b.Property(x => x.CountedBy).HasMaxLength(150);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.Ignore(x => x.Variance);
        b.Ignore(x => x.HasVariance);

        b.HasOne(x => x.CountPlan).WithMany(p => p.Tasks)
            .HasForeignKey(x => x.CountPlanId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Location).WithMany()
            .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InventoryStatus).WithMany()
            .HasForeignKey(x => x.InventoryStatusId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Lot).WithMany()
            .HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Serial).WithMany()
            .HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.LicensePlate).WithMany()
            .HasForeignKey(x => x.LicensePlateId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InventoryBalance).WithMany()
            .HasForeignKey(x => x.InventoryBalanceId).OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.InventoryAdjustment).WithMany()
            .HasForeignKey(x => x.InventoryAdjustmentId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.CountPlanId, x.Status });
        b.HasIndex(x => new { x.WarehouseId, x.Status });
    }
}

public class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> b)
    {
        b.ToTable("inventory_adjustments");
        b.HasKey(x => x.Id);

        b.Property(x => x.AdjustmentNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.SystemQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.CountedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.AdjustmentQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.QuantityBeforeApproval).HasPrecision(18, 4);
        b.Property(x => x.QuantityAfterApproval).HasPrecision(18, 4);
        b.Property(x => x.Reason).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.RequestedBy).HasMaxLength(150);
        b.Property(x => x.ApprovedBy).HasMaxLength(150);
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.Ignore(x => x.IsIncrease);

        b.HasOne(x => x.Location).WithMany()
            .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InventoryStatus).WithMany()
            .HasForeignKey(x => x.InventoryStatusId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Lot).WithMany()
            .HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Serial).WithMany()
            .HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.LicensePlate).WithMany()
            .HasForeignKey(x => x.LicensePlateId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InventoryBalance).WithMany()
            .HasForeignKey(x => x.InventoryBalanceId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.AccountId, x.AdjustmentNumber }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => x.CountTaskId);

        // A zero-quantity adjustment would write a no-op transaction.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_inventory_adjustments_nonzero", "\"AdjustmentQuantity\" <> 0"));
    }
}
