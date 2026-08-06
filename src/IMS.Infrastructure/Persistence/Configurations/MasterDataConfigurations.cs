using IMS.Domain.Entities.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IMS.Infrastructure.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("accounts");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.TimeZone).HasMaxLength(100);

        b.HasOne(x => x.Account)
            .WithMany(a => a.Warehouses)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Warehouse code is unique inside an account, not globally.
        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();
    }
}

public class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> b)
    {
        b.ToTable("zones");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ZoneType).HasConversion<int>();

        b.HasOne(x => x.Warehouse)
            .WithMany(w => w.Zones)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.ZoneType });
    }
}

public class LocationProfileConfiguration : IEntityTypeConfiguration<LocationProfile>
{
    public void Configure(EntityTypeBuilder<LocationProfile> b)
    {
        b.ToTable("location_profiles");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.LocationType).HasConversion<int>();

        b.Property(x => x.MaxWeight).HasPrecision(18, 3);
        b.Property(x => x.MaxVolume).HasPrecision(18, 3);
        b.Property(x => x.TemperatureMin).HasPrecision(6, 2);
        b.Property(x => x.TemperatureMax).HasPrecision(6, 2);

        b.HasOne(x => x.AllowedItemCategory)
            .WithMany()
            .HasForeignKey(x => x.AllowedItemCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();
    }
}

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("locations");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Aisle).HasMaxLength(20);
        b.Property(x => x.Bay).HasMaxLength(20);
        b.Property(x => x.Level).HasMaxLength(20);
        b.Property(x => x.Position).HasMaxLength(20);
        b.Property(x => x.LocationType).HasConversion<int>();

        b.Property(x => x.MaxWeight).HasPrecision(18, 3);
        b.Property(x => x.MaxVolume).HasPrecision(18, 3);
        b.Property(x => x.CoordinateX).HasPrecision(12, 3);
        b.Property(x => x.CoordinateY).HasPrecision(12, 3);
        b.Property(x => x.CoordinateZ).HasPrecision(12, 3);
        b.Property(x => x.DistanceToReceiving).HasPrecision(12, 3);
        b.Property(x => x.DistanceToPacking).HasPrecision(12, 3);
        b.Property(x => x.DistanceToShipping).HasPrecision(12, 3);

        // Computed properties are read from navigation state, never persisted.
        b.Ignore(x => x.EffectiveMaxWeight);
        b.Ignore(x => x.EffectiveMaxVolume);

        b.HasOne(x => x.Warehouse)
            .WithMany(w => w.Locations)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Zone)
            .WithMany(z => z.Locations)
            .HasForeignKey(x => x.ZoneId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.LocationProfile)
            .WithMany(p => p.Locations)
            .HasForeignKey(x => x.LocationProfileId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.ZoneId });

        // Supports GET /api/locations/available (§12).
        b.HasIndex(x => new { x.WarehouseId, x.IsActive, x.IsPutawayAllowed });
        b.HasIndex(x => new { x.WarehouseId, x.PickSequence });
    }
}

public class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> b)
    {
        b.ToTable("item_categories");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.HasOne(x => x.ParentCategory)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();
    }
}

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> b)
    {
        b.ToTable("units_of_measure");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();

        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class ItemMasterConfiguration : IEntityTypeConfiguration<ItemMaster>
{
    public void Configure(EntityTypeBuilder<ItemMaster> b)
    {
        b.ToTable("items");
        b.HasKey(x => x.Id);

        b.Property(x => x.Sku).HasMaxLength(100).IsRequired();
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);

        b.Property(x => x.Weight).HasPrecision(18, 3);
        b.Property(x => x.Length).HasPrecision(18, 3);
        b.Property(x => x.Width).HasPrecision(18, 3);
        b.Property(x => x.Height).HasPrecision(18, 3);
        b.Property(x => x.Volume).HasPrecision(18, 3);
        b.Property(x => x.MinimumStorageTemperature).HasPrecision(6, 2);
        b.Property(x => x.MaximumStorageTemperature).HasPrecision(6, 2);

        b.HasOne(x => x.Account)
            .WithMany(a => a.Items)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.BaseUom)
            .WithMany()
            .HasForeignKey(x => x.BaseUomId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.DefaultPutawayZone)
            .WithMany()
            .HasForeignKey(x => x.DefaultPutawayZoneId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(x => x.DefaultPickZone)
            .WithMany()
            .HasForeignKey(x => x.DefaultPickZoneId)
            .OnDelete(DeleteBehavior.SetNull);

        // Doc §4.1: "SKU, ayni account icerisinde unique olmalidir."
        b.HasIndex(x => new { x.AccountId, x.Sku }).IsUnique();
        b.HasIndex(x => new { x.AccountId, x.IsActive });
    }
}

public class AttributeDefinitionConfiguration : IEntityTypeConfiguration<AttributeDefinition>
{
    public void Configure(EntityTypeBuilder<AttributeDefinition> b)
    {
        b.ToTable("attribute_definitions");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.DataType).HasConversion<int>();

        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();

        // Lets the future slotting/picking engines find their relevant attributes cheaply.
        b.HasIndex(x => new { x.AccountId, x.IsSlottingRelevant });
        b.HasIndex(x => new { x.AccountId, x.IsPickingRelevant });
    }
}

public class ItemAttributeValueConfiguration : IEntityTypeConfiguration<ItemAttributeValue>
{
    public void Configure(EntityTypeBuilder<ItemAttributeValue> b)
    {
        b.ToTable("item_attribute_values");
        b.HasKey(x => x.Id);

        b.Property(x => x.TextValue).HasMaxLength(1000);
        b.Property(x => x.NumberValue).HasPrecision(18, 4);

        b.HasOne(x => x.Item)
            .WithMany(i => i.AttributeValues)
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.AttributeDefinition)
            .WithMany(a => a.Values)
            .HasForeignKey(x => x.AttributeDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        // One value per attribute per item.
        b.HasIndex(x => new { x.ItemId, x.AttributeDefinitionId }).IsUnique();

        // Supports filtering items by attribute value (AttributeDefinition.IsFilterable).
        b.HasIndex(x => new { x.AttributeDefinitionId, x.TextValue });
    }
}

public class ItemUomConfiguration : IEntityTypeConfiguration<ItemUom>
{
    public void Configure(EntityTypeBuilder<ItemUom> b)
    {
        b.ToTable("item_uoms");
        b.HasKey(x => x.Id);

        b.Property(x => x.ConversionQuantity).HasPrecision(18, 4).IsRequired();
        b.Property(x => x.Barcode).HasMaxLength(100);
        b.Property(x => x.Length).HasPrecision(18, 3);
        b.Property(x => x.Width).HasPrecision(18, 3);
        b.Property(x => x.Height).HasPrecision(18, 3);
        b.Property(x => x.Weight).HasPrecision(18, 3);

        b.HasOne(x => x.Item)
            .WithMany(i => i.ItemUoms)
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Uom)
            .WithMany()
            .HasForeignKey(x => x.UomId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ItemId, x.UomId }).IsUnique();

        // Doc §4.3: a conversion of zero or less would break every quantity calculation.
        b.ToTable(t => t.HasCheckConstraint(
            "ck_item_uoms_conversion_positive", "\"ConversionQuantity\" > 0"));
    }
}

public class ItemBarcodeConfiguration : IEntityTypeConfiguration<ItemBarcode>
{
    public void Configure(EntityTypeBuilder<ItemBarcode> b)
    {
        b.ToTable("item_barcodes");
        b.HasKey(x => x.Id);

        b.Property(x => x.Barcode).HasMaxLength(100).IsRequired();
        b.Property(x => x.BarcodeType).HasConversion<int>();

        b.HasOne(x => x.Item)
            .WithMany(i => i.Barcodes)
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Uom)
            .WithMany()
            .HasForeignKey(x => x.UomId)
            .OnDelete(DeleteBehavior.Restrict);

        // A barcode must resolve to exactly one item/UOM when scanned.
        b.HasIndex(x => x.Barcode).IsUnique();
        b.HasIndex(x => x.ItemId);
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("suppliers");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);

        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();
    }
}

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");
        b.HasKey(x => x.Id);

        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ShippingAddress).HasMaxLength(500);

        b.HasIndex(x => new { x.AccountId, x.Code }).IsUnique();
    }
}
