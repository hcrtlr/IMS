using IMS.Domain.Common;
using IMS.Domain.Enums;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §4.2 - definition half of the dynamic attribute system. Item properties vary by
/// industry, so they must not be hard-coded as ItemMaster columns. Examples given:
/// Color, Size, Brand, Material, Season, Model, CountryOfOrigin, BatteryType,
/// StorageClass, HazardClass, Gender, Collection.
/// </summary>
public class AttributeDefinition : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    /// <summary>Which of the four value columns on ItemAttributeValue is used.</summary>
    public AttributeDataType DataType { get; set; }

    /// <summary>Every item must supply a value for this attribute.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Attribute may be used as a search/filter facet.</summary>
    public bool IsFilterable { get; set; }

    /// <summary>
    /// Doc §4.2 - marks the attribute as input to a future slotting algorithm
    /// (e.g. keep Summer stock near the pick face in season).
    /// </summary>
    public bool IsSlottingRelevant { get; set; }

    /// <summary>
    /// Doc §4.2 - marks the attribute as input to a future picking algorithm
    /// (e.g. do not batch fragile items with heavy ones).
    /// </summary>
    public bool IsPickingRelevant { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ItemAttributeValue> Values { get; set; } = new List<ItemAttributeValue>();
}
