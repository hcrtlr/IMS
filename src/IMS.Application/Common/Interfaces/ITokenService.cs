using IMS.Domain.Entities.Security;

namespace IMS.Application.Common.Interfaces;

/// <summary>Issued access token plus the metadata the client needs.</summary>
public sealed record AccessToken(
    string Token,
    DateTimeOffset ExpiresAt,
    string TokenType = "Bearer");

/// <summary>Mints signed access tokens for authenticated users.</summary>
public interface ITokenService
{
    AccessToken CreateToken(ApplicationUser user);
}

/// <summary>Hashes and verifies passwords. Plaintext is never stored or logged.</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
