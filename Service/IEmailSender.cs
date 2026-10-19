namespace NIBRAS.API.Services;

/// <summary>One outbound message. The body carries no credential and no server secret.</summary>
public sealed record EmailMessage(string To, string Subject, string Body, bool IsHtml = false);

/// <summary>
/// The outcome of one delivery attempt. A failure is always reported as a failure: the senders
/// never turn an undelivered message into a success, so the caller can answer with a
/// controlled "not delivered" instead of pretending the mail went out.
/// </summary>
public sealed record EmailSendResult(bool Sent, string? FailureReason = null)
{
    public static EmailSendResult Success { get; } = new(true);

    public static EmailSendResult Failure(string reason) => new(false, reason);
}

/// <summary>
/// Delivery of the verification mails. Implementations must not throw for a delivery failure;
/// they report it through <see cref="EmailSendResult"/> so the flow can fail closed.
/// </summary>
public interface IEmailSender
{
    /// <summary>Attempts one delivery.</summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
