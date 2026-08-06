using IMS.Domain.Enums;

namespace IMS.Application.Features.MasterData;

// ---------------------------------------------------------------------------
// Doc §3 - organisation hierarchy: Account -> Warehouse -> Zone -> Location
// ---------------------------------------------------------------------------

public sealed record AccountDto(Guid Id, string Code, string Name, bool IsActive,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record CreateAccountRequest(string Code, string Name);
public sealed record UpdateAccountRequest(string Name, bool IsActive);

public sealed record WarehouseDto(Guid Id, Guid AccountId, string Code, string Name,
    string? Address, string? TimeZone, bool IsActive, int ZoneCount, int LocationCount);

public sealed record CreateWarehouseRequest(string Code, string Name, string? Address, string? TimeZone);
public sealed record UpdateWarehouseRequest(string Name, string? Address, string? TimeZone, bool IsActive);

public sealed record ZoneDto(Guid Id, Guid WarehouseId, string WarehouseCode, string Code, string Name,
    ZoneType ZoneType, int Priority, bool IsActive, int LocationCount);

public sealed record CreateZoneRequest(Guid WarehouseId, string Code, string Name, ZoneType ZoneType, int Priority);
public sealed record UpdateZoneRequest(string Name, ZoneType ZoneType, int Priority, bool IsActive);

// ---------------------------------------------------------------------------
// Doc §3.5 - location profile
// ---------------------------------------------------------------------------

public sealed record LocationProfileDto(Guid Id, string Code, string Name, LocationType LocationType,
    decimal? MaxWeight, decimal? MaxVolume, Guid? AllowedItemCategoryId, string? AllowedItemCategoryName,
    decimal? TemperatureMin, decimal? TemperatureMax,
    bool IsMixedItemAllowed, bool IsMixedLotAllowed, bool IsActive);

public sealed record CreateLocationProfileRequest(string Code, string Name, LocationType LocationType,
    decimal? MaxWeight, decimal? MaxVolume, Guid? AllowedItemCategoryId,
    decimal? TemperatureMin, decimal? TemperatureMax,
    bool IsMixedItemAllowed, bool IsMixedLotAllowed);

public sealed record UpdateLocationProfileRequest(string Name, LocationType LocationType,
    decimal? MaxWeight, decimal? MaxVolume, Guid? AllowedItemCategoryId,
    decimal? TemperatureMin, decimal? TemperatureMax,
    bool IsMixedItemAllowed, bool IsMixedLotAllowed, bool IsActive);

// ---------------------------------------------------------------------------
// Doc §3.4 + §10 - location, including the fields future algorithms need
// ---------------------------------------------------------------------------

public sealed record LocationDto(
    Guid Id, Guid WarehouseId, Guid ZoneId, string ZoneCode, ZoneType ZoneType,
    string Code, string? Aisle, string? Bay, string? Level, string? Position,
    LocationType LocationType, Guid? LocationProfileId, string? LocationProfileCode,
    int? PickSequence, int? PutawaySequence,
    decimal? CoordinateX, decimal? CoordinateY, decimal? CoordinateZ,
    decimal? MaxWeight, decimal? MaxVolume,
    bool IsPickable, bool IsPutawayAllowed, bool IsActive,
    decimal? DistanceToReceiving, decimal? DistanceToPacking, decimal? DistanceToShipping,
    int? AccessibilityScore, int? MaxConcurrentWorkers,
    decimal CurrentOnHandQuantity, int DistinctItemCount);

public sealed record CreateLocationRequest(
    Guid WarehouseId, Guid ZoneId, string Code,
    string? Aisle, string? Bay, string? Level, string? Position,
    LocationType LocationType, Guid? LocationProfileId,
    int? PickSequence, int? PutawaySequence,
    decimal? CoordinateX, decimal? CoordinateY, decimal? CoordinateZ,
    decimal? MaxWeight, decimal? MaxVolume,
    bool IsPickable, bool IsPutawayAllowed,
    decimal? DistanceToReceiving, decimal? DistanceToPacking, decimal? DistanceToShipping,
    int? AccessibilityScore, int? MaxConcurrentWorkers);

public sealed record UpdateLocationRequest(
    Guid ZoneId, string? Aisle, string? Bay, string? Level, string? Position,
    LocationType LocationType, Guid? LocationProfileId,
    int? PickSequence, int? PutawaySequence,
    decimal? CoordinateX, decimal? CoordinateY, decimal? CoordinateZ,
    decimal? MaxWeight, decimal? MaxVolume,
    bool IsPickable, bool IsPutawayAllowed, bool IsActive,
    decimal? DistanceToReceiving, decimal? DistanceToPacking, decimal? DistanceToShipping,
    int? AccessibilityScore, int? MaxConcurrentWorkers);

/// <summary>Filter for GET /api/locations/available (§12).</summary>
public sealed record AvailableLocationQuery
{
    public Guid WarehouseId { get; init; }
    public Guid? ZoneId { get; init; }
    public ZoneType? ZoneType { get; init; }
    public LocationType? LocationType { get; init; }

    /// <summary>When set, only locations whose profile can legally hold this item are returned.</summary>
    public Guid? ItemId { get; init; }

    /// <summary>Only locations that currently hold nothing.</summary>
    public bool EmptyOnly { get; init; }

    public bool PutawayOnly { get; init; } = true;
    public int Limit { get; init; } = 50;
}

// ---------------------------------------------------------------------------
// Doc §4 - item master
// ---------------------------------------------------------------------------

public sealed record ItemDto(
    Guid Id, Guid AccountId, string Sku, string Name, string? Description,
    Guid? CategoryId, string? CategoryName,
    Guid BaseUomId, string BaseUomCode,
    decimal? Weight, decimal? Length, decimal? Width, decimal? Height, decimal? Volume,
    bool IsLotTracked, bool IsSerialTracked, bool IsExpirationTracked, int? ShelfLifeDays,
    bool IsFragile, bool IsHazardous, bool IsTemperatureControlled,
    decimal? MinimumStorageTemperature, decimal? MaximumStorageTemperature,
    int? StackableQuantity, Guid? DefaultPutawayZoneId, Guid? DefaultPickZoneId,
    bool IsActive,
    IReadOnlyList<ItemAttributeValueDto> Attributes,
    IReadOnlyList<ItemUomDto> Uoms,
    IReadOnlyList<ItemBarcodeDto> Barcodes);

public sealed record ItemSummaryDto(
    Guid Id, string Sku, string Name, string? CategoryName, string BaseUomCode,
    bool IsLotTracked, bool IsSerialTracked, bool IsExpirationTracked, bool IsActive);

public sealed record CreateItemRequest(
    string Sku, string Name, string? Description,
    Guid? CategoryId, Guid BaseUomId,
    decimal? Weight, decimal? Length, decimal? Width, decimal? Height, decimal? Volume,
    bool IsLotTracked, bool IsSerialTracked, bool IsExpirationTracked, int? ShelfLifeDays,
    bool IsFragile, bool IsHazardous, bool IsTemperatureControlled,
    decimal? MinimumStorageTemperature, decimal? MaximumStorageTemperature,
    int? StackableQuantity, Guid? DefaultPutawayZoneId, Guid? DefaultPickZoneId);

public sealed record UpdateItemRequest(
    string Name, string? Description,
    Guid? CategoryId, Guid BaseUomId,
    decimal? Weight, decimal? Length, decimal? Width, decimal? Height, decimal? Volume,
    bool IsLotTracked, bool IsSerialTracked, bool IsExpirationTracked, int? ShelfLifeDays,
    bool IsFragile, bool IsHazardous, bool IsTemperatureControlled,
    decimal? MinimumStorageTemperature, decimal? MaximumStorageTemperature,
    int? StackableQuantity, Guid? DefaultPutawayZoneId, Guid? DefaultPickZoneId, bool IsActive);

// ---------------------------------------------------------------------------
// Doc §4.2 - dynamic attributes
// ---------------------------------------------------------------------------

public sealed record AttributeDefinitionDto(
    Guid Id, string Code, string Name, AttributeDataType DataType,
    bool IsRequired, bool IsFilterable, bool IsSlottingRelevant, bool IsPickingRelevant, bool IsActive);

public sealed record CreateAttributeDefinitionRequest(
    string Code, string Name, AttributeDataType DataType,
    bool IsRequired, bool IsFilterable, bool IsSlottingRelevant, bool IsPickingRelevant);

public sealed record UpdateAttributeDefinitionRequest(
    string Name, bool IsRequired, bool IsFilterable,
    bool IsSlottingRelevant, bool IsPickingRelevant, bool IsActive);

public sealed record ItemAttributeValueDto(
    Guid Id, Guid AttributeDefinitionId, string AttributeCode, string AttributeName,
    AttributeDataType DataType,
    string? TextValue, decimal? NumberValue, bool? BooleanValue, DateTimeOffset? DateValue);

/// <summary>Only the column matching the definition's DataType is read.</summary>
public sealed record SetItemAttributeRequest(
    Guid AttributeDefinitionId,
    string? TextValue, decimal? NumberValue, bool? BooleanValue, DateTimeOffset? DateValue);

// ---------------------------------------------------------------------------
// Doc §4.3 / §4.4 - UOM and barcodes
// ---------------------------------------------------------------------------

public sealed record UnitOfMeasureDto(Guid Id, string Code, string Name, string? Description, bool IsActive);
public sealed record CreateUnitOfMeasureRequest(string Code, string Name, string? Description);

public sealed record ItemUomDto(
    Guid Id, Guid UomId, string UomCode, string UomName, decimal ConversionQuantity,
    string? Barcode, decimal? Length, decimal? Width, decimal? Height, decimal? Weight,
    bool IsReceivingUom, bool IsPickingUom, bool IsShippingUom, bool IsActive);

public sealed record CreateItemUomRequest(
    Guid UomId, decimal ConversionQuantity, string? Barcode,
    decimal? Length, decimal? Width, decimal? Height, decimal? Weight,
    bool IsReceivingUom, bool IsPickingUom, bool IsShippingUom);

public sealed record ItemBarcodeDto(
    Guid Id, Guid UomId, string UomCode, string Barcode, BarcodeType BarcodeType,
    bool IsPrimary, bool IsActive);

public sealed record CreateItemBarcodeRequest(Guid UomId, string Barcode, BarcodeType BarcodeType, bool IsPrimary);

// ---------------------------------------------------------------------------
// Supporting master data (inferred - see docs/ASSUMPTIONS.md)
// ---------------------------------------------------------------------------

public sealed record ItemCategoryDto(Guid Id, string Code, string Name,
    Guid? ParentCategoryId, string? ParentCategoryName, bool IsActive);

public sealed record CreateItemCategoryRequest(string Code, string Name, Guid? ParentCategoryId);
public sealed record UpdateItemCategoryRequest(string Name, Guid? ParentCategoryId, bool IsActive);

public sealed record SupplierDto(Guid Id, string Code, string Name, string? ContactName,
    string? Email, string? Phone, string? Address, bool IsActive);

public sealed record CreateSupplierRequest(string Code, string Name, string? ContactName,
    string? Email, string? Phone, string? Address);

public sealed record UpdateSupplierRequest(string Name, string? ContactName,
    string? Email, string? Phone, string? Address, bool IsActive);

public sealed record CustomerDto(Guid Id, string Code, string Name, string? ContactName,
    string? Email, string? Phone, string? ShippingAddress, bool IsActive);

public sealed record CreateCustomerRequest(string Code, string Name, string? ContactName,
    string? Email, string? Phone, string? ShippingAddress);

public sealed record UpdateCustomerRequest(string Name, string? ContactName,
    string? Email, string? Phone, string? ShippingAddress, bool IsActive);
