using IMS.Domain.Entities.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IMS.Infrastructure.Persistence.Configurations;

public class OrderMasterConfiguration : IEntityTypeConfiguration<OrderMaster>
{
    public void Configure(EntityTypeBuilder<OrderMaster> b)
    {
        b.ToTable("orders");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Carrier).HasMaxLength(100);
        b.Property(x => x.ServiceLevel).HasMaxLength(100);
        b.Property(x => x.OrderType).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.Property(x => x.TotalWeight).HasPrecision(18, 3);
        b.Property(x => x.TotalVolume).HasPrecision(18, 3);
        b.Property(x => x.TotalQuantity).HasPrecision(18, 4);

        b.HasOne(x => x.Account).WithMany()
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.AccountId, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status });

        // Doc §10: priority + required ship date drive future order batching.
        b.HasIndex(x => new { x.WarehouseId, x.Priority, x.RequiredShipDate });
    }
}

public class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
{
    public void Configure(EntityTypeBuilder<OrderDetail> b)
    {
        b.ToTable("order_details");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.AllocatedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.PickedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.ShippedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.RequiredLotNumber).HasMaxLength(100);
        b.Property(x => x.RequiredSerialNumber).HasMaxLength(100);
        b.Property(x => x.Status).HasConversion<int>();

        b.Ignore(x => x.UnallocatedQuantity);
        b.Ignore(x => x.IsFullyAllocated);

        b.HasOne(x => x.Order).WithMany(o => o.Details)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Uom).WithMany()
            .HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.OrderId, x.LineNumber }).IsUnique();
        b.HasIndex(x => x.ItemId);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_order_details_ordered_positive", "\"OrderedQuantity\" > 0"));

        // Acceptance scenario 3: quantities must never run past what was ordered/allocated.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_order_details_quantity_progression",
            "\"AllocatedQuantity\" >= 0 AND \"PickedQuantity\" >= 0 AND \"ShippedQuantity\" >= 0 " +
            "AND \"AllocatedQuantity\" <= \"OrderedQuantity\" " +
            "AND \"PickedQuantity\" <= \"AllocatedQuantity\" " +
            "AND \"ShippedQuantity\" <= \"PickedQuantity\""));
    }
}

public class InventoryAllocationConfiguration : IEntityTypeConfiguration<InventoryAllocation>
{
    public void Configure(EntityTypeBuilder<InventoryAllocation> b)
    {
        b.ToTable("inventory_allocations");
        b.HasKey(x => x.Id);

        b.Property(x => x.AllocatedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.PickedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.AllocationStrategy).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();

        b.Ignore(x => x.OpenQuantity);

        b.HasOne(x => x.OrderDetail).WithMany(d => d.Allocations)
            .HasForeignKey(x => x.OrderDetailId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.InventoryBalance).WithMany()
            .HasForeignKey(x => x.InventoryBalanceId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Location).WithMany()
            .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Lot).WithMany()
            .HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Serial).WithMany()
            .HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.LicensePlate).WithMany()
            .HasForeignKey(x => x.LicensePlateId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.OrderDetailId);
        b.HasIndex(x => x.InventoryBalanceId);
        b.HasIndex(x => x.Status);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_inventory_allocations_quantity", "\"AllocatedQuantity\" > 0 AND \"PickedQuantity\" >= 0"));
    }
}

public class PickTaskConfiguration : IEntityTypeConfiguration<PickTask>
{
    public void Configure(EntityTypeBuilder<PickTask> b)
    {
        b.ToTable("pick_tasks");
        b.HasKey(x => x.Id);

        b.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.PickedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.AssignedTo).HasMaxLength(150);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.Ignore(x => x.ShortQuantity);

        b.HasOne(x => x.Order).WithMany(o => o.PickTasks)
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.OrderDetail).WithMany(d => d.PickTasks)
            .HasForeignKey(x => x.OrderDetailId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Allocation).WithMany(a => a.PickTasks)
            .HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FromLocation).WithMany()
            .HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.DestinationLocation).WithMany()
            .HasForeignKey(x => x.DestinationLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => x.OrderId);

        // Doc §7.4 PickBatchId - reserved for the future batching algorithm.
        b.HasIndex(x => new { x.PickBatchId, x.SequenceNumber });

        b.ToTable(t => t.HasCheckConstraint(
            "ck_pick_tasks_quantity", "\"Quantity\" > 0 AND \"PickedQuantity\" >= 0 AND \"PickedQuantity\" <= \"Quantity\""));
    }
}

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("shipments");
        b.HasKey(x => x.Id);

        b.Property(x => x.ShipmentNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Carrier).HasMaxLength(100);
        b.Property(x => x.ServiceLevel).HasMaxLength(100);
        b.Property(x => x.TrackingNumber).HasMaxLength(100);
        b.Property(x => x.ShippedBy).HasMaxLength(150);
        b.Property(x => x.TotalWeight).HasPrecision(18, 3);
        b.Property(x => x.TotalVolume).HasPrecision(18, 3);

        b.HasOne(x => x.Order).WithMany()
            .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ShipmentNumber).IsUnique();
        b.HasIndex(x => x.OrderId);
        b.HasIndex(x => x.CorrelationId);
    }
}

public class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> b)
    {
        b.ToTable("shipment_lines");
        b.HasKey(x => x.Id);

        b.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();

        b.HasOne(x => x.Shipment).WithMany(s => s.Lines)
            .HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.OrderDetail).WithMany()
            .HasForeignKey(x => x.OrderDetailId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ShipmentId);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_shipment_lines_quantity_positive", "\"Quantity\" > 0"));
    }
}
