namespace IMS.Domain.Common;

/// <summary>
/// Base for every persisted entity. Doc lists "Id / CreatedAt / UpdatedAt" on Account (§3.1)
/// and CreatedAt on most other tables; auditing is applied uniformly here.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

/// <summary>Adds create/update auditing, stamped centrally by the DbContext.</summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Marks an entity that belongs to a tenant Account. Doc §3: "tablolarda AccountId ve
/// WarehouseId alanlarinin bulunmasi onerilir" - every query is scoped by this.
/// </summary>
public interface IAccountScoped
{
    Guid AccountId { get; set; }
}

/// <summary>Marks an entity scoped to a single warehouse.</summary>
public interface IWarehouseScoped
{
    Guid WarehouseId { get; set; }
}
