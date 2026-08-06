using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Referenced by ItemMaster.CategoryId (§4.1) and LocationProfile.AllowedItemCategory (§3.5),
/// but never defined as its own table in the document. Modelled minimally, with optional
/// self-nesting for a category tree. ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class ItemCategory : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;

    public Guid? ParentCategoryId { get; set; }
    public ItemCategory? ParentCategory { get; set; }
    public ICollection<ItemCategory> Children { get; set; } = new List<ItemCategory>();

    public bool IsActive { get; set; } = true;
}
