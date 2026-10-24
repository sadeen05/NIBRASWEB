using Microsoft.Extensions.Options;
using NibrasWeb.Enums;

namespace NIBRAS.API.Services;

/// <summary>
/// Delivers the confirmation mail of an authenticated address change. The plaintext token is
/// created by <see cref="IEmailVerificationService"/>, handed to the mailer in process and then
/// forgotten: it is never returned to the caller, logged or stored. The link carries it in the
/// URL fragment, which no browser sends to a server.
/// </summary>
public sealed class EmailChangeService : IEmailChangeService
{
    private readonly IEmailVerificationService _verification;
    private readonly IEmailSender _emailSender;
    private readonly IUserSecurityEventService _audit;
    private readonly EmailOptions _options;
    private readonly ILogger<EmailChangeService> _logger;
    private readonly TimeProvider _timeProvider;

    public EmailChangeService(
        IEmailVerificationService verification,
        IEmailSender emailSender,
        IUserSecurityEventService audit,
        IOptions<EmailOptions> options,
        ILogger<EmailChangeService> logger,
        TimeProvider timeProvider)
    {
        _verification = verification;
        _emailSender = emailSender;
        _audit = audit;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<EmailChangeResult> RequestAsync(
        int userId,
        string newEmail,
        string currentPassword,
        string? ip = null,
        CancellationToken cancellationToken = default)
    {
        var address = newEmail?.Trim() ?? string.Empty;

        var issued = await _verification.IssueEmailChangeTokenAsync(
            userId,
            address,
            currentPassword,
            cancellationToken);

        if (!issued.Succeeded || issued.Issued is null)
        {
            var outcome = issued.Outcome switch
            {
                VerificationOutcome.InvalidCurrentPassword => EmailChangeOutcome.InvalidCurrentPassword,
                VerificationOutcome.AddressUnchanged => EmailChangeOutcome.AddressUnchanged,
                VerificationOutcome.EmailInUse => EmailChangeOutcome.EmailInUse,
                VerificationOutcome.InvalidAddress => EmailChangeOutcome.InvalidAddress,
                VerificationOutcome.CooldownActive or VerificationOutcome.DailyCapReached =>
                    EmailChangeOutcome.Throttled,
                VerificationOutcome.UserNotFound
                    or VerificationOutcome.NotVerificationEligible
                    or VerificationOutcome.RoleNotAllowed => EmailChangeOutcome.AccountNotEligible,
                _ => EmailChangeOutcome.AccountNotEligible,
            };

            _logger.LogWarning(
                "The address change for account {UserId} was refused ({Outcome}).",
                userId,
                outcome);
            return new EmailChangeResult(outcome, issued.FailureReason);
        }

        var message = BuildMessage(address, issued.Issued.Plaintext, issued.Issued.ExpiresAt);

        var sent = await VerificationDelivery.SendAsync(_verification, _emailSender, userId, NibrasWeb.Enums.EmailVerificationPurposes.EmailChange, issued.Issued, message, cancellationToken);
        if (!sent.Sent)
        {
            // The active address is untouched and the pending address stays unconfirmed. A new
            // request retires this token, so a failed delivery never leaves two usable links.
            await RecordAuditAsync(
                userId,
                UserSecurityEventTypes.VerificationEmailDeliveryFailed,
                ip,
                cancellationToken);
            _logger.LogWarning(
                "The address-change confirmation for account {UserId} could not be delivered.",
                userId);
            return new EmailChangeResult(EmailChangeOutcome.DeliveryFailed, sent.FailureReason);
        }

        await RecordAuditAsync(
            userId,
            UserSecurityEventTypes.VerificationEmailSent,
            ip,
            cancellationToken);
        _logger.LogInformation(
            "The address-change confirmation for account {UserId} was handed over for delivery.",
            userId);
        return new EmailChangeResult(EmailChangeOutcome.Sent);
    }

    /// <summary>
    /// The bilingual address-change mail. It deliberately does not repeat the old address: the
    /// message may be read on a device that does not belong to the account holder.
    /// </summary>
    private EmailMessage BuildMessage(string recipient, string token, DateTime expiresAt)
    {
        var hours = Math.Max(1, (int)Math.Ceiling((expiresAt - _timeProvider.GetUtcNow().UtcDateTime).TotalHours));
        var link = VerificationLinkBuilder.Build(_options.FrontendBaseUrl, _options.EmailChangePath, token);
        var subject = "Confirm your new NIBRAS address / تأكيد عنوانك الجديد في نِبراس";
        var body =
$@"Hello,

A request was made to use this address for a NIBRAS account. Confirm the new address within {hours} hours:

{link}

The link can be used once. The active address of the account stays unchanged until this link is confirmed. If you did not request this change, you can ignore this message and keep using the current address.

---
مرحبًا،

تم طلب استخدام هذا العنوان لحساب في نِبراس. أكّد العنوان الجديد خلال {hours} ساعة:

{link}

يمكن استخدام الرابط مرة واحدة فقط. يبقى العنوان الحالي للحساب دون تغيير حتى يتم تأكيد هذا الرابط. إذا لم تطلب هذا التغيير، يمكنك تجاهل هذه الرسالة والاستمرار في استخدام العنوان الحالي.";

        return new EmailMessage(recipient, subject, body);
    }

    /// <summary>
    /// A failing audit write must not change the delivery outcome: the mail was either handed
    /// over or not. The failure of the audit write itself is logged.
    /// </summary>
    private async Task RecordAuditAsync(
        int userId,
        string eventType,
        string? ip,
        CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(
                userId,
                eventType,
                actorId: null,
                ip: ip,
                details: "email_change",
                cancellationToken: cancellationToken);
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
}
