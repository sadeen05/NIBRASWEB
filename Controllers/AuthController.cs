using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailVerificationService _emailVerification;

    public AuthController(
        IAuthService authService,
        ICurrentUser currentUser,
        IEmailVerificationService emailVerification)
    {
        _authService = authService;
        _currentUser = currentUser;
        _emailVerification = emailVerification;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<ActionResult<AuthUserSummary>> Register(RegisterRequest request)
    {
        try
        {
            return Ok(await _authService.RegisterAsync(request));
        }
        catch (DuplicateRegistrationException)
        {
            return Conflict(new { message = "Email or phone is already registered" });
        }
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        return result is null
            ? Unauthorized(new { message = "Invalid email or password" })
            : Ok(result);
    }

    [HttpGet("me")]
    public async Task<ActionResult<AuthUserSummary>> Me()
    {
        if (_currentUser.Id is not int id)
        {
            return Unauthorized();
        }

        var user = await _authService.GetCurrentUserAsync(id);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>
    /// Activation of a new Admin account. The single-use invitation token is the only
    /// credential, so the endpoint is anonymous, but it is rate-limited like the other
    /// authentication endpoints. Every failed activation answers with the same generic
    /// message, so the endpoint does not reveal whether a token ever existed; the precise
    /// reason is kept in the server-side audit trail.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("activation")]
    [HttpPost("activate-admin")]
    public async Task<IActionResult> ActivateAdmin(ActivateAdminRequest request)
    {
        var result = await _emailVerification.RedeemAdminActivationAsync(request.Token, request.Password);

        return result.Outcome switch
        {
            VerificationOutcome.Succeeded => NoContent(),
            VerificationOutcome.EmailInUse => Conflict(new
            {
                message = "The address of the account is already in use.",
                errorCode = "ACTIVATION_CONFLICT",
            }),
            VerificationOutcome.AlreadyActivated
                or VerificationOutcome.RoleNotAllowed
                or VerificationOutcome.UserNotFound => Conflict(new
                {
                    message = "The activation link is not valid.",
                    errorCode = "ACTIVATION_INVALID",
                }),
            _ => BadRequest(new
            {
                message = "The activation link is not valid.",
                errorCode = "ACTIVATION_INVALID",
            }),
        };
    }
}
