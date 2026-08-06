using IMS.Domain.Entities.Inbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IMS.Infrastructure.Persistence.Configurations;

public class InboundOrderConfiguration : IEntityTypeConfiguration<InboundOrder>
{
    public void Configure(EntityTypeBuilder<InboundOrder> b)
    {
        b.ToTable("inbound_orders");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Supplier).WithMany()
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.AccountId, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status });
    }
}

public class InboundOrderDetailConfiguration : IEntityTypeConfiguration<InboundOrderDetail>
{
    public void Configure(EntityTypeBuilder<InboundOrderDetail> b)
    {
        b.ToTable("inbound_order_details");
        b.HasKey(x => x.Id);

        b.Property(x => x.ExpectedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.ReceivedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.ExpectedLotNumber).HasMaxLength(100);

        b.Ignore(x => x.OutstandingQuantity);
        b.Ignore(x => x.IsFullyReceived);

        b.HasOne(x => x.InboundOrder).WithMany(o => o.Details)
            .HasForeignKey(x => x.InboundOrderId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Uom).WithMany()
            .HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.InboundOrderId, x.LineNumber }).IsUnique();

        b.ToTable(t => t.HasCheckConstraint(
            "ck_inbound_order_details_expected_positive", "\"ExpectedQuantity\" > 0"));
    }
}

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.ToTable("receipts");
        b.HasKey(x => x.Id);

        b.Property(x => x.ReceiptNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.ReceivedBy).HasMaxLength(150);
        b.Property(x => x.Notes).HasMaxLength(1000);

        b.HasOne(x => x.Warehouse).WithMany()
            .HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InboundOrder).WithMany(o => o.Receipts)
            .HasForeignKey(x => x.InboundOrderId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ReceivingLocation).WithMany()
            .HasForeignKey(x => x.ReceivingLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ReceiptNumber).IsUnique();
        b.HasIndex(x => x.InboundOrderId);
        b.HasIndex(x => x.CorrelationId);
    }
}

public class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> b)
    {
        b.ToTable("receipt_lines");
        b.HasKey(x => x.Id);

        b.Property(x => x.ReceivedQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.BaseQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.PutawayQuantity).HasPrecision(18, 4).IsRequired();

        b.Ignore(x => x.PendingPutawayQuantity);

        b.HasOne(x => x.Receipt).WithMany(r => r.Lines)
            .HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.InboundOrderDetail).WithMany(d => d.ReceiptLines)
            .HasForeignKey(x => x.InboundOrderDetailId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ReceivedUom).WithMany()
            .HasForeignKey(x => x.ReceivedUomId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Lot).WithMany()
            .HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Serial).WithMany()
            .HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.LicensePlate).WithMany()
            .HasForeignKey(x => x.LicensePlateId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.InventoryStatus).WithMany()
            .HasForeignKey(x => x.InventoryStatusId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ReceiptId);
        b.HasIndex(x => x.InboundOrderDetailId);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_receipt_lines_quantity_positive", "\"ReceivedQuantity\" > 0 AND \"BaseQuantity\" > 0"));
    }
}

public class PutawayTaskConfiguration : IEntityTypeConfiguration<PutawayTask>
{
    public void Configure(EntityTypeBuilder<PutawayTask> b)
    {
        b.ToTable("putaway_tasks");
        b.HasKey(x => x.Id);

        b.Property(x => x.Quantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.RecommendationReason).HasMaxLength(500);
        b.Property(x => x.AssignedTo).HasMaxLength(150);

        b.HasOne(x => x.ReceiptLine).WithMany(l => l.PutawayTasks)
            .HasForeignKey(x => x.ReceiptLineId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.FromLocation).WithMany()
            .HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.SuggestedLocation).WithMany()
            .HasForeignKey(x => x.SuggestedLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ActualLocation).WithMany()
            .HasForeignKey(x => x.ActualLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => x.ReceiptLineId);

        b.ToTable(t => t.HasCheckConstraint(
            "ck_putaway_tasks_quantity_positive", "\"Quantity\" > 0"));
    }
}
