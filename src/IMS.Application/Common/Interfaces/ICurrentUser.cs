using IMS.Domain.Enums;

namespace IMS.Application.Common.Interfaces;

/// <summary>
/// The authenticated caller, resolved from the JWT by the API layer. Injected wherever
/// the application needs to stamp PerformedBy (§5.6) or enforce account scoping.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Username { get; }

    /// <summary>Tenant the caller belongs to. Every account-scoped query filters on this.</summary>
    Guid? AccountId { get; }

    UserRole? Role { get; }

    bool IsAuthenticated { get; }

    /// <summary>True when the caller holds at least one of the given roles.</summary>
    bool IsInRole(params UserRole[] roles);
}
