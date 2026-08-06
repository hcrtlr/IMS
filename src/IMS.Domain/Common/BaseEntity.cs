namespace IMS.Domain.Common;

/// <summary>
/// Base for every persisted entity. Doc lists "Id / CreatedAt / UpdatedAt" on Account (§3.1)
/// and CreatedAt on most other tables; auditing is applied uniformly here.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Assigned client-side so an entity can be referenced (and its id logged or returned)
    /// before it is saved.
    ///
    /// CAUTION: because the key is always populated, EF Core classifies an untracked entity
    /// discovered through a navigation property as Modified rather than Added, and emits an
    /// UPDATE against a row that does not exist. When attaching a child to a parent that has
    /// ALREADY been saved, add it through its DbSet - `_db.Children.Add(child)` - not only
    /// through `parent.Children.Add(child)`. Building a whole new graph before calling
    /// `_db.Parents.Add(parent)` is safe, because the entire graph is then Added.
    /// </summary>
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
