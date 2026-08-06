using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Referenced by OrderMaster.CustomerId (§7.1) but never defined as a table in the
/// document. Modelled minimally so the foreign key has referential integrity.
/// ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class Customer : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ShippingAddress { get; set; }
    public bool IsActive { get; set; } = true;
}
