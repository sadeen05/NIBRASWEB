using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// Issues the activation invitation of a new administrator account and delivers it by mail.
/// <para>
/// The plaintext token exists only inside this method: it is handed to the mailer in process
/// and is never returned to the caller, never logged and never part of an audit event. The
/// activation link carries the token in the URL <b>fragment</b>, which no browser ever sends
/// to a server, so the token cannot appear in an access log, a proxy log or a referrer.
/// </para>
/// <para>
/// A delivery failure leaves the account unverified and unusable, is recorded in the audit
/// trail, and is recoverable: the next invitation issues a new link and retires the failed
/// one, so a retry can never leave two usable invitations and never creates a second account.
/// </para>
/// </summary>
public sealed class AdminInvitationService : IAdminInvitationService
{
    private readonly NebrasdbContext _context;
    private readonly IEmailVerificationService _verification;
    private readonly IEmailSender _emailSender;
    private readonly IUserSecurityEventService _audit;
    private readonly EmailOptions _options;
    private readonly ILogger<AdminInvitationService> _logger;

    public AdminInvitationService(
        NebrasdbContext context,
        IEmailVerificationService verification,
        IEmailSender emailSender,
        IUserSecurityEventService audit,
        IOptions<EmailOptions> options,
        ILogger<AdminInvitationService> logger)
    {
        _context = context;
        _verification = verification;
        _emailSender = emailSender;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AdminInvitationResult> SendAsync(
        int adminUserId,
        int? actorId = null,
        string? ip = null,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == adminUserId && !u.IsDeleted, cancellationToken);
        if (account is null)
        {
            return new AdminInvitationResult(AdminInvitationOutcome.AccountNotEligible, "user_missing");
        }

        // The token service re-checks the role, the deletion state and the verification state
        // of the account, and it retires the earlier invitations in the same transaction. The
        // resend path is used for both the first invitation and every later one, so the resend
        // cooldown and the daily cap apply to an administrator invitation as well - a
        // super administrator cannot use it to flood an address with invitations.
        var issued = await _verification.ResendTokenAsync(
            adminUserId,
            EmailVerificationPurposes.AdminActivation,
            cancellationToken);
        if (!issued.Succeeded || issued.Issued is null)
        {
            _logger.LogWarning(
                "The activation invitation for admin account {UserId} could not be issued.",
                adminUserId);
            return new AdminInvitationResult(AdminInvitationOutcome.AccountNotEligible, issued.FailureReason);
        }

        var message = BuildMessage(account.Email, issued.Issued.Plaintext, issued.Issued.ExpiresAt);

        // The token travels from here straight into the mailer.
        var sent = await _emailSender.SendAsync(message, cancellationToken);
        if (!sent.Sent)
        {
            // The account stays unverified and unusable. The failure is recorded; a new
            // invitation can be issued at any time (it retires this one).
            await RecordAuditAsync(
                adminUserId,
                UserSecurityEventTypes.VerificationEmailDeliveryFailed,
                actorId,
                ip,
                cancellationToken);
            _logger.LogWarning(
                "The activation invitation for admin account {UserId} could not be delivered.",
                adminUserId);
            return new AdminInvitationResult(AdminInvitationOutcome.DeliveryFailed, sent.FailureReason);
        }

        await RecordAuditAsync(
            adminUserId,
            UserSecurityEventTypes.VerificationEmailSent,
            actorId,
            ip,
            cancellationToken);

        // Only the outcome is logged: never the address of the recipient, never the token.
        _logger.LogInformation("The activation invitation for admin account {UserId} was handed over for delivery.", adminUserId);
        return new AdminInvitationResult(AdminInvitationOutcome.Sent);
    }

    /// <summary>
    /// Records the delivery outcome. A failing audit write must not change the outcome of the
    /// delivery: the mail was either handed over or not, and the answer to the caller is the
    /// same either way. The failure of the audit write itself is logged.
    /// </summary>
    private async Task RecordAuditAsync(
        int adminUserId,
        string eventType,
        int? actorId,
        string? ip,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(
                adminUserId,
                eventType,
                actorId,
                ip,
                "admin_invitation",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The security audit event {EventType} for account {UserId} could not be written.", eventType, adminUserId);
        }
    }

    /// <summary>
    /// The bilingual invitation. The link carries the token in the fragment, so it never
    /// reaches a server; the frontend reads it, redeems it and strips it from the address bar.
    /// </summary>
    private EmailMessage BuildMessage(string recipient, string token, DateTime expiresAt)
    {
        var hours = Math.Max(1, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalHours));
        var link = BuildActivationLink(token);
        var subject = "NIBRAS administrator account activation / تفعيل حساب مشرف في NIBRAS";
        var body =
$@"Hello,

A super administrator created a NIBRAS administrator account for you. Choose your own password and activate the account within {hours} hours:

{link}

The link can be used once. If you did not expect this invitation, ignore this message: the account stays locked until it is activated.

---
مرحبًا،

أنشأ مشرف أعلى حساب مشرف في NIBRAS نيابةً عنك. اختر كلمة المرور الخاصة بك وفعّل الحساب خلال {hours} ساعة:

{link}

يمكن استخدام الرابط مرة واحدة فقط. إذا لم تتوقع هذه الدعوة، تجاهل هذه الرسالة: يبقى الحساب مقفلًا حتى يتم تفعيله.";

        return new EmailMessage(recipient, subject, body);
    }

    private string BuildActivationLink(string token)
    {
        var baseUrl = _options.FrontendBaseUrl.TrimEnd('/');
        var path = _options.ActivationPath.StartsWith('/') ? _options.ActivationPath : "/" + _options.ActivationPath;

        // The fragment is never transmitted to a server, so the token cannot end up in an
        // access log, a proxy log or a referrer header.
        return $"{baseUrl}{path}#token={Uri.EscapeDataString(token)}";
    }
}
