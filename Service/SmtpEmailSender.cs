using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace NIBRAS.API.Services;

/// <summary>
/// Delivers verification mails through an SMTP server. The password is read from the
/// configuration and is never written to a log, an exception message or a result; only the
/// outcome and a masked recipient are logged.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.SmtpEnableSsl,
                // A mail server that does not answer must not hold the request open: the
                // delivery is reported as failed and the invitation can be sent again.
                Timeout = 30_000,
            };

            if (!string.IsNullOrWhiteSpace(_options.SmtpUser))
            {
                client.Credentials = new NetworkCredential(_options.SmtpUser, _options.SmtpPassword);
            }

            using var mail = new MailMessage
            {
                From = new MailAddress(_options.FromAddress),
                Subject = message.Subject,
                Body = message.Body,
                IsBodyHtml = message.IsHtml,
            };
            mail.To.Add(message.To);

            await client.SendMailAsync(mail, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Verification email delivery timed out.");
            return EmailSendResult.Failure("smtp_timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SmtpException ex) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is distinct from a transport timeout.
            throw new OperationCanceledException("The email delivery was cancelled by the caller.", ex, cancellationToken);
        }
        catch (SmtpException) when (deadline.IsCancellationRequested)
        {
            // The transport surfaced the timeout as an SmtpException. It is a genuine deadline
            // expiry only because the send deadline actually elapsed; a server rejection or a
            // fast transport failure does not reach this filter.
            _logger.LogWarning("Verification email delivery timed out.");
            return EmailSendResult.Failure("smtp_timeout");
        }
        catch (SmtpException ex)
        {
            // The message is not repeated: it can carry an activation link.
            _logger.LogWarning("Verification email delivery failed ({ExceptionType}).", ex.GetType().Name);
            return EmailSendResult.Failure("smtp_delivery_failed");
        }
        catch (TimeoutException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Verification email delivery timed out.");
            return EmailSendResult.Failure("smtp_timeout");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Verification email delivery failed ({ExceptionType}).", ex.GetType().Name);
            return EmailSendResult.Failure("email_delivery_failed");
        }

        _logger.LogInformation("Verification email delivered to {Recipient}.", MaskRecipient(message.To));
        return EmailSendResult.Success;
    }

    /// <summary>
    /// Masks the local part of the address so a delivery log does not become a list of
    /// customer addresses.
    /// </summary>
    private static string MaskRecipient(string address)
    {
        var at = address.IndexOf('@');
        return at <= 0 ? "***" : string.Concat(address.AsSpan(0, 1), "***", address.AsSpan(at));
    }
}
