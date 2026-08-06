using IMS.Api.Security;
using IMS.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMS.Api.Controllers;

/// <summary>
/// Authentication and user administration. Not part of the source specification -
/// see docs/ASSUMPTIONS.md.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    /// <summary>Exchanges username and password for a JWT access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    /// <summary>Returns the profile of the currently authenticated user.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.ReadOnly)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
        => Ok(await _auth.GetCurrentUserAsync(ct));

    /// <summary>Changes the current user's own password and invalidates existing tokens.</summary>
    [HttpPost("change-password")]
    [Authorize(Policy = Policies.ReadOnly)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await _auth.ChangePasswordAsync(request, ct);
        return NoContent();
    }

    /// <summary>Lists the users in the caller's account.</summary>
    [HttpGet("users")]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> ListUsers(CancellationToken ct)
        => Ok(await _auth.ListUsersAsync(ct));

    /// <summary>Creates a user inside the caller's account.</summary>
    [HttpPost("users")]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<UserDto>> CreateUser(CreateUserRequest request, CancellationToken ct)
    {
        var user = await _auth.CreateUserAsync(request, ct);
        return CreatedAtAction(nameof(ListUsers), new { id = user.Id }, user);
    }

    /// <summary>Updates a user's profile, role or active state.</summary>
    [HttpPut("users/{id:guid}")]
    [Authorize(Policy = Policies.Administration)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> UpdateUser(Guid id, UpdateUserRequest request, CancellationToken ct)
        => Ok(await _auth.UpdateUserAsync(id, request, ct));
}
