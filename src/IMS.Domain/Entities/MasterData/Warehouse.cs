using IMS.Domain.Common;

namespace IMS.Domain.Entities.MasterData;

/// <summary>Doc §3.2 - a physical depot.</summary>
public class Warehouse : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;

    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Address { get; set; }

    /// <summary>IANA time zone id, e.g. "Europe/Istanbul".</summary>
    public string? TimeZone { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<Zone> Zones { get; set; } = new List<Zone>();
    public ICollection<Location> Locations { get; set; } = new List<Location>();
}
