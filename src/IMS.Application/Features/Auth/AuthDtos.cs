using IMS.Domain.Enums;

namespace IMS.Application.Features.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    UserDto User);

public sealed record UserDto(
    Guid Id,
    string Username,
    string Email,
    string FullName,
    UserRole Role,
    Guid AccountId,
    string AccountCode,
    Guid? DefaultWarehouseId,
    bool IsActive,
    DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(
    string Username,
    string Email,
    string FullName,
    string Password,
    UserRole Role,
    Guid? DefaultWarehouseId);

public sealed record UpdateUserRequest(
    string Email,
    string FullName,
    UserRole Role,
    Guid? DefaultWarehouseId,
    bool IsActive);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
