using IMS.Application.Common.Interfaces;
using IMS.Domain.Entities.Security;
using IMS.Domain.Enums;
using IMS.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IMS.Application.Features.Auth;

/// <summary>Raised when credentials do not authenticate. Maps to HTTP 401.</summary>
public class AuthenticationFailedException : DomainException
{
    public override string ErrorCode => "AUTHENTICATION_FAILED";
    public AuthenticationFailedException(string message) : base(message) { }
}

/// <summary>
/// Authentication and user administration. Not part of the source document - see
/// docs/ASSUMPTIONS.md for why this exists and how roles map to warehouse duties.
/// </summary>
public class AuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher hasher,
        ITokenService tokens,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users
            .Include(u => u.Account)
            .FirstOrDefaultAsync(u => u.Username == request.Username, ct);

        // Verify against a dummy hash when the user is unknown so response timing does
        // not reveal whether the username exists.
        var storedHash = user?.PasswordHash ?? DummyHash;
        var passwordValid = _hasher.Verify(request.Password, storedHash);

        if (user is null || !passwordValid)
        {
            _logger.LogWarning("Failed login attempt for username {Username}", request.Username);
            throw new AuthenticationFailedException("Invalid username or password.");
        }

        if (!user.IsActive)
            throw new AuthenticationFailedException("This account has been deactivated.");

        if (!user.Account.IsActive)
            throw new AuthenticationFailedException("The owning account has been deactivated.");

        user.LastLoginAt = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);

        var token = _tokens.CreateToken(user);
        _logger.LogInformation("User {Username} authenticated successfully", user.Username);

        return new LoginResponse(token.Token, token.TokenType, token.ExpiresAt, ToDto(user));
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var accountId = RequireAccount();

        if (await _db.Users.AnyAsync(u => u.Username == request.Username, ct))
            throw new DuplicateEntityException($"Username '{request.Username}' is already taken.");

        if (await _db.Users.AnyAsync(u => u.Email == request.Email, ct))
            throw new DuplicateEntityException($"Email '{request.Email}' is already registered.");

        if (request.DefaultWarehouseId.HasValue)
            await EnsureWarehouseInAccountAsync(request.DefaultWarehouseId.Value, accountId, ct);

        var user = new ApplicationUser
        {
            AccountId = accountId,
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = _hasher.Hash(request.Password),
            Role = request.Role,
            DefaultWarehouseId = request.DefaultWarehouseId,
            IsActive = true
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created user {Username} with role {Role}", user.Username, user.Role);

        var created = await _db.Users
            .Include(u => u.Account)
            .FirstAsync(u => u.Id == user.Id, ct);

        return ToDto(created);
    }

    public async Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken ct = default)
    {
        var accountId = RequireAccount();

        var users = await _db.Users
            .Include(u => u.Account)
            .Where(u => u.AccountId == accountId)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);

        return users.Select(ToDto).ToList();
    }

    public async Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var accountId = RequireAccount();

        var user = await _db.Users
            .Include(u => u.Account)
            .FirstOrDefaultAsync(u => u.Id == id && u.AccountId == accountId, ct)
            ?? throw new NotFoundException(nameof(ApplicationUser), id);

        if (user.Email != request.Email &&
            await _db.Users.AnyAsync(u => u.Email == request.Email && u.Id != id, ct))
            throw new DuplicateEntityException($"Email '{request.Email}' is already registered.");

        if (request.DefaultWarehouseId.HasValue)
            await EnsureWarehouseInAccountAsync(request.DefaultWarehouseId.Value, accountId, ct);

        // Guard against removing the last active administrator of an account.
        if (user.Role == UserRole.Admin && (request.Role != UserRole.Admin || !request.IsActive))
        {
            var otherAdmins = await _db.Users.CountAsync(
                u => u.AccountId == accountId && u.Role == UserRole.Admin && u.IsActive && u.Id != id, ct);

            if (otherAdmins == 0)
                throw new BusinessRuleViolationException(
                    "Cannot demote or deactivate the last active administrator of this account.");
        }

        user.Email = request.Email;
        user.FullName = request.FullName;
        user.Role = request.Role;
        user.DefaultWarehouseId = request.DefaultWarehouseId;
        user.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId
            ?? throw new AuthenticationFailedException("No authenticated user.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new AuthenticationFailedException("Current password is incorrect.");

        user.PasswordHash = _hasher.Hash(request.NewPassword);

        // Invalidates every token issued before this change.
        user.SecurityStamp++;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Password changed for user {Username}", user.Username);
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId
            ?? throw new AuthenticationFailedException("No authenticated user.");

        var user = await _db.Users
            .Include(u => u.Account)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException(nameof(ApplicationUser), userId);

        return ToDto(user);
    }

    private Guid RequireAccount()
        => _currentUser.AccountId
           ?? throw new AuthenticationFailedException("The current token carries no account claim.");

    private async Task EnsureWarehouseInAccountAsync(Guid warehouseId, Guid accountId, CancellationToken ct)
    {
        var belongs = await _db.Warehouses
            .AnyAsync(w => w.Id == warehouseId && w.AccountId == accountId, ct);

        if (!belongs)
            throw new NotFoundException($"Warehouse '{warehouseId}' was not found in this account.");
    }

    private static UserDto ToDto(ApplicationUser u) => new(
        u.Id, u.Username, u.Email, u.FullName, u.Role,
        u.AccountId, u.Account?.Code ?? string.Empty,
        u.DefaultWarehouseId, u.IsActive, u.LastLoginAt);

    /// <summary>A valid BCrypt hash of a random value, used only for timing equalisation.</summary>
    private const string DummyHash = "$2a$12$C6UzMDM.H6dfI/f/IKcEe.4pnqIQ0FmRHrVCyeYFrEsxbcbAvvV5S";
}
