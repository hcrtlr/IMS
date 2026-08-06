namespace IMS.Infrastructure.Identity;

/// <summary>
/// JWT signing/validation settings, bound from configuration section "Jwt".
/// The secret must come from configuration (user-secrets, environment variable or a
/// secret store) - it is never hard-coded and never logged.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "IMS.Api";
    public string Audience { get; set; } = "IMS.Client";

    /// <summary>HMAC-SHA256 signing key. Must be at least 32 bytes.</summary>
    public string Secret { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
}
