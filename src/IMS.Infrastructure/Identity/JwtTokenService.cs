using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IMS.Application.Common.Interfaces;
using IMS.Domain.Entities.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace IMS.Infrastructure.Identity;

/// <summary>
/// Issues HMAC-SHA256 signed JWTs. The AccountId claim is what every account-scoped
/// query filters on, so it is mandatory in every token.
/// </summary>
public class JwtTokenService : ITokenService
{
    /// <summary>Custom claim carrying the caller's tenant.</summary>
    public const string AccountIdClaim = "ims:account_id";

    /// <summary>Custom claim carrying the user's default warehouse, if set.</summary>
    public const string WarehouseIdClaim = "ims:warehouse_id";

    private readonly JwtSettings _settings;
    private readonly IDateTimeProvider _clock;

    public JwtTokenService(IOptions<JwtSettings> settings, IDateTimeProvider clock)
    {
        _settings = settings.Value;
        _clock = clock;
    }

    public AccessToken CreateToken(ApplicationUser user)
    {
        var now = _clock.UtcNow;
        var expires = now.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(AccountIdClaim, user.AccountId.ToString()),
            // Invalidates tokens issued before a password change.
            new("ims:security_stamp", user.SecurityStamp.ToString())
        };

        if (user.DefaultWarehouseId.HasValue)
            claims.Add(new Claim(WarehouseIdClaim, user.DefaultWarehouseId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

/// <summary>BCrypt password hashing with a per-password salt.</summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed stored hash must fail closed, not throw to the caller.
            return false;
        }
    }
}
