using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NibrasWeb.Enums;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailVerificationService _emailVerification;
    private readonly IEmailChangeService _emailChange;
    private readonly IVerificationDeliveryQueue _deliveryQueue;

    public AuthController(
        IAuthService authService,
        ICurrentUser currentUser,
        IEmailVerificationService emailVerification,
        IEmailChangeService emailChange,
        IVerificationDeliveryQueue deliveryQueue)
    {
        _authService = authService;
        _currentUser = currentUser;
        _emailVerification = emailVerification;
        _emailChange = emailChange;
        _deliveryQueue = deliveryQueue;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<ActionResult<AuthUserSummary>> Register(RegisterRequest request)
    {
        try
        {
            var registered = await _authService.RegisterAsync(request, HttpContext.RequestAborted);

            // 201 Created, per the documented contract. There is no client-accessible
            // retrieval action for a freshly registered account: GET /api/auth/me needs the
            // authentication that registration does not establish, and GET /api/users/{id}
            // is restricted to administrators. A Location header would therefore point at a
            // resource the caller cannot read, so the status alone reports the creation.
            return StatusCode(StatusCodes.Status201Created, registered);
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
        var result = await _authService.LoginAsync(request, HttpContext.RequestAborted);
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

    /// <summary>
    /// Confirmation of the address of a new Landlord or Investor account. The single-use
    /// verification token is the only credential, so the endpoint is anonymous, but it has its
    /// own rate-limit budget, so a flood of confirmation attempts can never exhaust the
    /// sign-in and registration budget. Only a registration token is accepted: an activation
    /// token and an address-change token are refused like any other unusable token, and every
    /// unusable token is answered with the same generic message, so the endpoint does not
    /// reveal whether a token ever existed.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("verification")]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request)
    {
        // The purpose is fixed: only a token that was issued for a registration verification
        // is consumed here. The consumption itself re-checks the hash, the purpose, the
        // expiry, the single use, the role, the deletion state, the eligibility and the
        // address the token is bound to, and it commits the consumed token, the verified
        // address and the audit event in one transaction.
        var result = await _emailVerification.ConsumeTokenAsync(
            request.Token,
            EmailVerificationPurposes.Registration);

        return result.Outcome switch
        {
            VerificationOutcome.Succeeded => NoContent(),
            // The address was claimed by another account in the meantime. The caller proved the
            // mailbox, so the conflict is a fact it may know.
            VerificationOutcome.EmailInUse => Conflict(new
            {
                message = "The address of the account is already in use.",
                errorCode = "VERIFICATION_CONFLICT",
            }),
            _ => BadRequest(new
            {
                message = "The verification link is not valid.",
                errorCode = "VERIFICATION_INVALID",
            }),
        };
    }

    /// <summary>
    /// Requests an address change for the authenticated account. The current password is
    /// required, and the active address is not replaced until the new address is confirmed
    /// through the link sent to it. The endpoint is authenticated, but has its own rate-limit
    /// budget because it can cause a mail to be sent.
    /// </summary>
    [Authorize]
    [EnableRateLimiting("email-change")]
    [HttpPost("change-email")]
    public async Task<IActionResult> ChangeEmail(ChangeEmailRequest request)
    {
        if (_currentUser.Id is not int userId)
        {
            return Unauthorized();
        }

        var result = await _emailChange.RequestAsync(
            userId,
            request.NewEmail,
            request.CurrentPassword,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            HttpContext.RequestAborted);

        return result.Outcome switch
        {
            EmailChangeOutcome.Sent => Accepted(new
            {
                message = "A confirmation message has been sent to the new address. The current address stays active until the new one is confirmed.",
            }),
            EmailChangeOutcome.InvalidCurrentPassword => BadRequest(new
            {
                message = "The current password is not correct.",
                errorCode = "EMAIL_CHANGE_PASSWORD_INVALID",
            }),
            EmailChangeOutcome.AddressUnchanged => Conflict(new
            {
                message = "The new address is already the active address of this account.",
                errorCode = "EMAIL_CHANGE_UNCHANGED",
            }),
            EmailChangeOutcome.EmailInUse => Conflict(new
            {
                message = "The new address is already in use.",
                errorCode = "EMAIL_CHANGE_CONFLICT",
            }),
            EmailChangeOutcome.InvalidAddress => BadRequest(new
            {
                message = "The new address is not valid.",
                errorCode = "EMAIL_CHANGE_INVALID_ADDRESS",
            }),
            EmailChangeOutcome.Throttled => StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Too many address-change requests. Please wait before trying again.",
                errorCode = "EMAIL_CHANGE_THROTTLED",
            }),
            EmailChangeOutcome.DeliveryFailed => StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "The confirmation message could not be delivered. The current address is unchanged; please try again later.",
                errorCode = "EMAIL_CHANGE_DELIVERY_FAILED",
            }),
            _ => Conflict(new
            {
                message = "The address change cannot be requested for this account.",
                errorCode = "EMAIL_CHANGE_NOT_ELIGIBLE",
            }),
        };
    }

    /// <summary>
    /// Confirms the new address of an authenticated account. The token is the only credential,
    /// so the endpoint is anonymous, but it has its own rate-limit budget. On success the active
    /// address is replaced and every JWT issued before the change stops validating, because the
    /// session carries the old verification epoch.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("verification")]
    [HttpPost("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChange(VerifyEmailRequest request)
    {
        var result = await _emailVerification.ConsumeTokenAsync(
            request.Token,
            EmailVerificationPurposes.EmailChange);

        return result.Outcome switch
        {
            VerificationOutcome.Succeeded => NoContent(),
            VerificationOutcome.EmailInUse => Conflict(new
            {
                message = "The address of the account is already in use.",
                errorCode = "EMAIL_CHANGE_CONFLICT",
            }),
            _ => BadRequest(new
            {
                message = "The address-change link is not valid.",
                errorCode = "EMAIL_CHANGE_INVALID",
            }),
        };
    }

    /// <summary>
    /// Requests a new verification mail for an address that still has to be confirmed. The
    /// endpoint is anonymous and answers with the same generic 202 in every case: a missing
    /// account, an account that is already verified, an ineligible account, an active cooldown
    /// and a reached daily cap are indistinguishable, so it never reveals whether an address
    /// belongs to an account. The per-account limits are enforced by the token service under
    /// the account lock, and the endpoint has its own external rate-limit budget.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting("verification-resend")]
    [HttpPost("resend-verification")]
    public IActionResult ResendVerification(ResendVerificationRequest request)
    {
        // The work is handed to the managed delivery worker, which performs it with its own
        // scope: the answer is the same in every case, and its duration never depends on the
        // account state or on the mail hand-off. A queue that is full is not reported to the
        // caller either - the refusal is counted and logged by the queue.
        _deliveryQueue.TryEnqueue(new VerificationDeliveryRequest(
            request.Email,
            HttpContext.Connection.RemoteIpAddress?.ToString()));

        return Accepted(new
        {
            message = "If the address belongs to an account that still has to be verified, a verification message has been sent.",
        });
    }
}
