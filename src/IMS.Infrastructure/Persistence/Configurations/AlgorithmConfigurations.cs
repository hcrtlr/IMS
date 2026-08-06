using IMS.Domain.Entities.Algorithms;
using IMS.Domain.Entities.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IMS.Infrastructure.Persistence.Configurations;

public class AlgorithmConfigurationConfiguration : IEntityTypeConfiguration<AlgorithmConfiguration>
{
    public void Configure(EntityTypeBuilder<AlgorithmConfiguration> b)
    {
        b.ToTable("algorithm_configurations");
        b.HasKey(x => x.Id);

        b.Property(x => x.AlgorithmName).HasMaxLength(100).IsRequired();
        b.Property(x => x.ParameterKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.ParameterValue).HasMaxLength(2000).IsRequired();
        b.Property(x => x.ValueType).HasMaxLength(20).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);

        b.HasIndex(x => new { x.AccountId, x.WarehouseId, x.AlgorithmName, x.ParameterKey })
            .IsUnique();
    }
}

public class SlottingRecommendationConfiguration : IEntityTypeConfiguration<SlottingRecommendation>
{
    public void Configure(EntityTypeBuilder<SlottingRecommendation> b)
    {
        b.ToTable("slotting_recommendations");
        b.HasKey(x => x.Id);

        b.Property(x => x.Score).HasPrecision(6, 2);
        b.Property(x => x.RecommendationReason).HasMaxLength(500);
        b.Property(x => x.AlgorithmName).HasMaxLength(100);
        b.Property(x => x.AlgorithmVersion).HasMaxLength(50);
        b.Property(x => x.DecidedBy).HasMaxLength(150);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CurrentLocation).WithMany()
            .HasForeignKey(x => x.CurrentLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.RecommendedLocation).WithMany()
            .HasForeignKey(x => x.RecommendedLocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.WarehouseId, x.ItemId, x.GeneratedAt });
    }
}

public class PickingPlanConfiguration : IEntityTypeConfiguration<PickingPlan>
{
    public void Configure(EntityTypeBuilder<PickingPlan> b)
    {
        b.ToTable("picking_plans");
        b.HasKey(x => x.Id);

        b.Property(x => x.PlanNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.AlgorithmName).HasMaxLength(100);
        b.Property(x => x.AlgorithmVersion).HasMaxLength(50);
        b.Property(x => x.BatchingStrategy).HasMaxLength(50);
        b.Property(x => x.AssignedTo).HasMaxLength(150);
        b.Property(x => x.EstimatedTravelDistance).HasPrecision(12, 3);
        b.Property(x => x.ActualTravelDistance).HasPrecision(12, 3);

        b.HasIndex(x => x.PlanNumber).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.GeneratedAt });
    }
}

public class PickingRouteStopConfiguration : IEntityTypeConfiguration<PickingRouteStop>
{
    public void Configure(EntityTypeBuilder<PickingRouteStop> b)
    {
        b.ToTable("picking_route_stops");
        b.HasKey(x => x.Id);

        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.Property(x => x.DistanceFromPrevious).HasPrecision(12, 3);

        b.HasOne(x => x.PickingPlan).WithMany(p => p.Stops)
            .HasForeignKey(x => x.PickingPlanId).OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Location).WithMany()
            .HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.PickTask).WithMany()
            .HasForeignKey(x => x.PickTaskId).OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.PickingPlanId, x.StopSequence }).IsUnique();
    }
}

public class OrderHistorySnapshotConfiguration : IEntityTypeConfiguration<OrderHistorySnapshot>
{
    public void Configure(EntityTypeBuilder<OrderHistorySnapshot> b)
    {
        b.ToTable("order_history");
        b.HasKey(x => x.Id);

        b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(100).IsRequired();
        b.Property(x => x.Carrier).HasMaxLength(100);
        b.Property(x => x.ServiceLevel).HasMaxLength(100);
        b.Property(x => x.OrderType).HasConversion<int>();
        b.Property(x => x.OrderedQuantity).HasPrecision(18, 4);
        b.Property(x => x.ShippedQuantity).HasPrecision(18, 4);

        b.HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        // Demand-history lookups for future slotting: "how often is this item ordered?"
        b.HasIndex(x => new { x.WarehouseId, x.ItemId, x.ShippedAt });
        b.HasIndex(x => new { x.WarehouseId, x.ShippedAt });

        // One snapshot row per shipped order line.
        b.HasIndex(x => x.OrderDetailId).IsUnique();
    }
}

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);

        b.Property(x => x.Username).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.Role).HasConversion<int>();

        b.HasOne(x => x.Account).WithMany()
            .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Username).IsUnique();
        b.HasIndex(x => x.Email).IsUnique();
    }
}
