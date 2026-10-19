namespace NIBRAS.API.Services;

/// <summary>
/// The fail-closed sender used when no delivery is configured. It never reports a success:
/// a flow that needs a delivered mail must fail with a controlled "not delivered" answer
/// instead of creating an account that pretends to have been verified.
/// </summary>
public sealed class DisabledEmailSender : IEmailSender
{
    private readonly ILogger<DisabledEmailSender> _logger;

    public DisabledEmailSender(ILogger<DisabledEmailSender> logger) => _logger = logger;

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogError(
            "Email delivery is disabled (Email:DeliveryMode={Mode}); the verification flow cannot complete.",
            EmailOptions.ModeDisabled);
        return Task.FromResult(EmailSendResult.Failure("email_delivery_disabled"));
    }
}
