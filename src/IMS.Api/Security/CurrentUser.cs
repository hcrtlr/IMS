using System.Security.Claims;
using IMS.Application.Common.Interfaces;
using IMS.Domain.Enums;
using IMS.Infrastructure.Identity;

namespace IMS.Api.Security;

/// <summary>
/// Resolves the authenticated caller from the JWT claims on the current HTTP request.
/// Everything downstream (audit stamping, account scoping) reads identity through this.
/// </summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Username => Principal?.FindFirstValue(ClaimTypes.Name);

    public Guid? AccountId =>
        Guid.TryParse(Principal?.FindFirstValue(JwtTokenService.AccountIdClaim), out var id) ? id : null;

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    public bool IsInRole(params UserRole[] roles)
    {
        var current = Role;
        return current.HasValue && roles.Contains(current.Value);
    }
}
