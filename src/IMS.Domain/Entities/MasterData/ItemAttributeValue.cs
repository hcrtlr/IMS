using IMS.Domain.Common;
using IMS.Domain.Enums;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Doc §4.2 - value half of the dynamic attribute system. One typed column per data
/// type, of which exactly one is populated according to the definition's DataType.
///
/// The doc's field list omits the FK to AttributeDefinition; it is required for the
/// structure to work and is added here. ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class ItemAttributeValue : AuditableEntity
{
    public Guid ItemId { get; set; }
    public ItemMaster Item { get; set; } = null!;

    public Guid AttributeDefinitionId { get; set; }
    public AttributeDefinition AttributeDefinition { get; set; } = null!;

    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTimeOffset? DateValue { get; set; }

    /// <summary>Returns the populated value column as an object, based on the definition's data type.</summary>
    public object? GetValue() => AttributeDefinition?.DataType switch
    {
        AttributeDataType.Text => TextValue,
        AttributeDataType.Number => NumberValue,
        AttributeDataType.Boolean => BooleanValue,
        AttributeDataType.Date => DateValue,
        _ => null
    };

    /// <summary>True when the column matching the given data type carries a value.</summary>
    public bool HasValueFor(AttributeDataType dataType) => dataType switch
    {
        AttributeDataType.Text => !string.IsNullOrWhiteSpace(TextValue),
        AttributeDataType.Number => NumberValue.HasValue,
        AttributeDataType.Boolean => BooleanValue.HasValue,
        AttributeDataType.Date => DateValue.HasValue,
        _ => false
    };
}
