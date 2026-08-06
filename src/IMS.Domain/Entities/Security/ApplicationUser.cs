using IMS.Domain.Common;
using IMS.Domain.Enums;

namespace IMS.Domain.Entities.Security;

/// <summary>
/// Not part of the source document - authentication and authorization were requested
/// separately. Users are scoped to an Account, which is what enforces tenant isolation
/// on every query. See docs/ASSUMPTIONS.md.
/// </summary>
public class ApplicationUser : AuditableEntity, IAccountScoped
{
    public Guid AccountId { get; set; }
    public MasterData.Account Account { get; set; } = null!;

    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string FullName { get; set; } = null!;

    /// <summary>BCrypt hash. The plaintext password is never stored or logged.</summary>
    public string PasswordHash { get; set; } = null!;

    public UserRole Role { get; set; } = UserRole.Viewer;

    /// <summary>Optional default warehouse, used to prefill operational screens.</summary>
    public Guid? DefaultWarehouseId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Incremented on password change to invalidate previously issued tokens.</summary>
    public int SecurityStamp { get; set; }
}
