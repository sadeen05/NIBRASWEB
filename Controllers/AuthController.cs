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

    public AuthController(IAuthService authService, ICurrentUser currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
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
}
