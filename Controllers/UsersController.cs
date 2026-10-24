using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NIBRAS.API.DTOs;
using NIBRAS.API.Services;
using NIBRAS.Models;

namespace NIBRAS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.Admin + "," + RoleNames.SuperAdmin)]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ICurrentUser _currentUser;
    private readonly IAdminInvitationService _adminInvitation;
    private readonly IRegistrationVerificationService _registrationVerification;

    public UsersController(
        IUserService userService,
        ICurrentUser currentUser,
        IAdminInvitationService adminInvitation,
        IRegistrationVerificationService registrationVerification)
    {
        _userService = userService;
        _currentUser = currentUser;
        _adminInvitation = adminInvitation;
        _registrationVerification = registrationVerification;
    }

    [HttpGet]
    public async Task<ActionResult<UserListResponse>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? role = null,
        [FromQuery] bool? isApproved = null)
    {
        return Ok(await _userService.GetAllAsync(pageNumber, pageSize, role, isApproved));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetById(int id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(user);
    }

    /// <summary>
    /// The investor-approval eligibility and the durable approval history. The history is read
    /// from the append-only security events, so it keeps every earlier decision even after a
    /// later administrator approves the account again.
    /// </summary>
    [HttpGet("{id}/approval")]
    public async Task<ActionResult<UserApprovalStatusDto>> GetApprovalStatus(int id)
    {
        var status = await _userService.GetApprovalStatusAsync(id);
        if (status == null) return NotFound();
        return Ok(status);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request)
    {
        if (request.RoleId is < 1 or > 3 ||
            (_currentUser.Role == RoleNames.Admin && request.RoleId == 3))
        {
            throw new ForbiddenException("You are not authorized to create this user role.");
        }

        var created = await _userService.CreateAsync(request, HttpContext.RequestAborted);
        var user = created.User;

        // The body keeps the flat user shape that every existing consumer of
        // POST /api/users already reads, and adds what happened to the activation
        // invitation of a new administrator account and to the verification of a new
        // landlord or investor account. The account exists; the statuses only report what
        // happened at the mailer and never claim that a message was received.
        return CreatedAtAction(
            nameof(GetById),
            new { id = user.Id },
            new
            {
                user.Id,
                user.FullName,
                user.Email,
                user.Phone,
                user.Role,
                user.CreatedAt,
                user.IsApproved,
                user.CompanyName,
                user.CompanyRegistrationNumber,
                created.Invitation,
                created.InvitationMessage,
                created.Verification,
                created.VerificationMessage,
            });
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(int id)
    {
        if (_currentUser.Id is not int approvedById)
        {
            return Unauthorized();
        }

        var result = await _userService.ApproveAsync(id, approvedById);
        return result ? NoContent() : NotFound();
    }

    [HttpPost("{id}/reject-approval")]
    public async Task<IActionResult> RejectApproval(int id, RejectInvestorApprovalRequest request)
    {
        return await _userService.RejectApprovalAsync(id, request) ? NoContent() : NotFound();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest request)
    {
        var result = await _userService.UpdateAsync(id, request);
        if (!result) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _userService.DeleteAsync(id);
        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// (Re)sends the activation invitation of a new administrator account. Only a SuperAdmin
    /// may trigger it. The token is delivered to the account by mail and is never returned in
    /// the response; a new invitation retires the previous one, so only the newest link works.
    /// </summary>
    [HttpPost("{id}/admin-invitation")]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> SendAdminInvitation(int id)
    {
        var result = await _adminInvitation.SendAsync(
            id,
            _currentUser.Id,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return result.Outcome switch
        {
            AdminInvitationOutcome.Sent => Accepted(new
            {
                message = "The activation invitation was sent to the account.",
            }),
            AdminInvitationOutcome.DeliveryFailed => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "The invitation could not be delivered. The account stays inactive; the invitation can be sent again.",
                }),
            _ => Conflict(new
            {
                message = "The account cannot receive an activation invitation.",
            }),
        };
    }

    /// <summary>
    /// (Re)sends the verification mail of an unverified Landlord or Investor account. Only a
    /// SuperAdmin may trigger it. The token is delivered to the account by mail and is never
    /// returned in the response; a new verification retires the previous one, so only the
    /// newest link works. The endpoint never verifies an account by itself, and it can never
    /// activate an administrator account - that flow has its own invitation.
    /// </summary>
    [HttpPost("{id}/verification")]
    [Authorize(Roles = RoleNames.SuperAdmin)]
    public async Task<IActionResult> SendVerification(int id)
    {
        var result = await _registrationVerification.SendAsync(
            id,
            _currentUser.Id,
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return result.Outcome switch
        {
            RegistrationVerificationOutcome.Sent => Accepted(new
            {
                message = "The verification mail was handed over for delivery.",
            }),
            RegistrationVerificationOutcome.DeliveryFailed => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new
                {
                    message = "The verification mail could not be delivered. The account stays unverified; the verification can be sent again.",
                }),
            _ => Conflict(new
            {
                message = "The account cannot receive a verification mail.",
            }),
        };
    }
}
