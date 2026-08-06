using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>
/// Referenced by InboundOrder.SupplierId (§6.1) but never defined as a table in the
/// document. Modelled minimally so the foreign key has referential integrity.
/// ASSUMPTION - see docs/ASSUMPTIONS.md.
/// </summary>
public class Supplier : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? ContactName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}
