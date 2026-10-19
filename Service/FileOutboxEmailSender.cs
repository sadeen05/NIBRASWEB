using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NIBRAS.API.Services;

/// <summary>
/// Development-only delivery sink: every message is written as one JSON file into a private
/// per-run directory, so a test can pick the activation link up without a mail server. The
/// directory must be outside any Git working tree, and the sink refuses to run outside
/// Development. The message is never written to the log.
/// </summary>
public sealed class FileOutboxEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<FileOutboxEmailSender> _logger;

    public FileOutboxEmailSender(
        IOptions<EmailOptions> options,
        IHostEnvironment environment,
        ILogger<FileOutboxEmailSender> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            _logger.LogError("The development outbox is not available outside Development.");
            return Task.FromResult(EmailSendResult.Failure("outbox_not_available"));
        }

        if (string.IsNullOrWhiteSpace(_options.OutboxPath) ||
            !Path.IsPathRooted(_options.OutboxPath) ||
            EmailOutboxLocation.IsInsideGitWorkingTree(_options.OutboxPath))
        {
            _logger.LogError("The development outbox path is not usable; it must be an absolute path outside a Git working tree.");
            return Task.FromResult(EmailSendResult.Failure("outbox_path_not_usable"));
        }

        try
        {
            Directory.CreateDirectory(_options.OutboxPath);
            EmailOutboxLocation.RestrictDirectoryToOwner(_options.OutboxPath);

            // The file name carries no address or subject, only a timestamp and a random id.
            var fileName = $"mail-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
            var filePath = Path.Combine(_options.OutboxPath, fileName);

            var payload = JsonSerializer.Serialize(
                new
                {
                    sentAtUtc = DateTime.UtcNow,
                    to = message.To,
                    subject = message.Subject,
                    body = message.Body,
                    isHtml = message.IsHtml,
                },
                new JsonSerializerOptions { WriteIndented = true });

            // Written to a temporary file and renamed, so a reader never sees a half message.
            var temporaryPath = filePath + ".tmp";
            File.WriteAllText(temporaryPath, payload);
            File.Move(temporaryPath, filePath, overwrite: true);
            EmailOutboxLocation.RestrictFileToOwner(filePath);
        }
        catch (OperationCanceledException)
        {
            // A cancelled write is not a confirmed non-delivery: the message may or may not
            // have been written. The cancellation is passed on, so the caller can tell the
            // two apart and no failed delivery is recorded for it - the same rule the SMTP
            // sender follows.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Writing a message to the development outbox failed.");
            return Task.FromResult(EmailSendResult.Failure("outbox_write_failed"));
        }

        return Task.FromResult(EmailSendResult.Success);
    }
}
