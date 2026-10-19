using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NIBRAS.Models;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// Issues the registration verification of a new Landlord or Investor account and delivers it
/// by mail.
/// <para>
/// The plaintext token exists only inside this method: it is handed to the mailer in process
/// and is never returned to the caller, never logged and never part of an audit event. The
/// verification link carries the token in the URL <b>fragment</b>, which no browser ever sends
/// to a server, so the token cannot appear in an access log, a proxy log or a referrer.
/// </para>
/// <para>
/// The token service re-checks the role and the verification state of the account, retires the
/// earlier tokens in the same transaction and enforces the resend cooldown and the daily cap
/// under the account lock, so two parallel requests cannot both pass them.
/// </para>
/// <para>
/// A delivery failure leaves the account unverified, is recorded in the audit trail, and is
/// recoverable: the next request issues a new link and retires the failed one, so a retry can
/// never leave two usable links and never creates a second account. Nothing is ever verified
/// automatically, and no account is ever deleted automatically.
/// </para>
/// </summary>
public sealed class RegistrationVerificationService : IRegistrationVerificationService
{
    private readonly NebrasdbContext _context;
    private readonly IEmailVerificationService _verification;
    private readonly IEmailSender _emailSender;
    private readonly IUserSecurityEventService _audit;
    private readonly EmailOptions _options;
    private readonly ILogger<RegistrationVerificationService> _logger;

    public RegistrationVerificationService(
        NebrasdbContext context,
        IEmailVerificationService verification,
        IEmailSender emailSender,
        IUserSecurityEventService audit,
        IOptions<EmailOptions> options,
        ILogger<RegistrationVerificationService> logger)
    {
        _context = context;
        _verification = verification;
        _emailSender = emailSender;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RegistrationVerificationResult> SendAsync(
        int userId,
        int? actorId = null,
        string? ip = null,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (account is null)
        {
            return new RegistrationVerificationResult(
                RegistrationVerificationOutcome.AccountNotEligible,
                "user_missing");
        }

        // The resend path is used for the first verification and for every later one, so the
        // resend cooldown and the daily cap apply to a registration verification as well: a
        // request cannot be used to flood one address with mails. The limits are read under
        // the account lock, so two parallel requests cannot both pass them.
        var issued = await _verification.ResendTokenAsync(
            userId,
            EmailVerificationPurposes.Registration,
            cancellationToken);
        if (!issued.Succeeded || issued.Issued is null)
        {
            _logger.LogWarning(
                "The registration verification for account {UserId} could not be issued.",
                userId);

            var outcome = issued.Outcome switch
            {
                VerificationOutcome.CooldownActive => RegistrationVerificationOutcome.Throttled,
                VerificationOutcome.DailyCapReached => RegistrationVerificationOutcome.Throttled,
                _ => RegistrationVerificationOutcome.AccountNotEligible,
            };

            return new RegistrationVerificationResult(outcome, issued.FailureReason);
        }

        var message = BuildMessage(account.Email, issued.Issued.Plaintext, issued.Issued.ExpiresAt);

        // The token travels from here straight into the mailer.
        var sent = await _emailSender.SendAsync(message, cancellationToken);
        if (!sent.Sent)
        {
            // The account stays unverified. The failure is recorded; a new verification can
            // be requested at any time (it retires this one).
            await RecordAuditAsync(
                userId,
                UserSecurityEventTypes.VerificationEmailDeliveryFailed,
                actorId,
                ip,
                cancellationToken);
            _logger.LogWarning(
                "The registration verification for account {UserId} could not be delivered.",
                userId);
            return new RegistrationVerificationResult(
                RegistrationVerificationOutcome.DeliveryFailed,
                sent.FailureReason);
        }

        await RecordAuditAsync(
            userId,
            UserSecurityEventTypes.VerificationEmailSent,
            actorId,
            ip,
            cancellationToken);

        // Only the outcome is logged: never the address of the recipient, never the token.
        _logger.LogInformation(
            "The registration verification for account {UserId} was handed over for delivery.",
            userId);
        return new RegistrationVerificationResult(RegistrationVerificationOutcome.Sent);
    }

    /// <summary>
    /// Records the delivery outcome. A failing audit write must not change the outcome of the
    /// delivery: the mail was either handed over or not, and the answer to the caller is the
    /// same either way. The failure of the audit write itself is logged.
    /// </summary>
    private async Task RecordAuditAsync(
        int userId,
        string eventType,
        int? actorId,
        string? ip,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(
                userId,
                eventType,
                actorId,
                ip,
                "registration_verification",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "The security audit event {EventType} for account {UserId} could not be written.",
                eventType,
                userId);
        }
    }

    /// <summary>
    /// The bilingual verification mail. The link carries the token in the fragment, so it never
    /// reaches a server; the frontend reads it, redeems it and strips it from the address bar.
    /// </summary>
    private EmailMessage BuildMessage(string recipient, string token, DateTime expiresAt)
    {
        var hours = Math.Max(1, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalHours));
        var link = VerificationLinkBuilder.Build(_options.FrontendBaseUrl, _options.VerificationPath, token);
        var subject = "Confirm your NIBRAS account / تأكيد حسابك في نِبراس";
        var body =
$@"Hello,

Welcome to NIBRAS. Confirm this email address to activate your account within {hours} hours:

{link}

The link can be used once. If you did not create this account, you can ignore this message: the account stays inactive until the address is confirmed.

---
مرحبًا،

أهلًا بك في نِبراس. أكّد عنوان بريدك الإلكتروني لتفعيل حسابك خلال {hours} ساعة:

{link}

يمكن استخدام الرابط مرة واحدة فقط. إذا لم تنشئ هذا الحساب، فيمكنك تجاهل هذه الرسالة: يبقى الحساب غير مفعّل حتى يتم تأكيد العنوان.";

        return new EmailMessage(recipient, subject, body);
    }
}
