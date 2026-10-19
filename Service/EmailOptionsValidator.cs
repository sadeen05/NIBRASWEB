using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NIBRAS.API.Services;

/// <summary>
/// Validates the email configuration at startup and refuses to let a host run in a state where
/// verification could silently be skipped. The failure messages never contain a configured
/// value, so a misconfiguration can be reported without leaking a credential.
/// </summary>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    private readonly IHostEnvironment _environment;

    public EmailOptionsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, EmailOptions options)
    {
        var failures = new List<string>();

        if (!options.IsSmtp && !options.IsFileOutbox && !options.IsDisabled)
        {
            failures.Add(
                $"Email:DeliveryMode must be one of {EmailOptions.ModeSmtp}, {EmailOptions.ModeFileOutbox} or {EmailOptions.ModeDisabled}.");
        }

        // Outside Development the only acceptable mode is SMTP: the development outbox must not
        // be reachable from a real host, and verification must not be switchable off there.
        if (!_environment.IsDevelopment() && !options.IsSmtp)
        {
            failures.Add(
                $"Email:DeliveryMode must be {EmailOptions.ModeSmtp} outside Development: email verification cannot be disabled and the development outbox is not allowed there.");
        }

        if (options.IsSmtp)
        {
            if (string.IsNullOrWhiteSpace(options.SmtpHost))
            {
                failures.Add($"Email:SmtpHost is required when the delivery mode is {EmailOptions.ModeSmtp}.");
            }

            if (options.SmtpPort is < 1 or > 65535)
            {
                failures.Add("Email:SmtpPort must be between 1 and 65535.");
            }

            if (string.IsNullOrWhiteSpace(options.FromAddress))
            {
                failures.Add($"Email:FromAddress is required when the delivery mode is {EmailOptions.ModeSmtp}.");
            }
            else if (!options.FromAddress.Contains('@'))
            {
                failures.Add("Email:FromAddress must be an email address.");
            }

            if (!string.IsNullOrWhiteSpace(options.SmtpUser) && string.IsNullOrEmpty(options.SmtpPassword))
            {
                failures.Add("Email:SmtpPassword is required when Email:SmtpUser is configured.");
            }
        }

        if (options.IsFileOutbox)
        {
            if (string.IsNullOrWhiteSpace(options.OutboxPath))
            {
                failures.Add($"Email:OutboxPath is required when the delivery mode is {EmailOptions.ModeFileOutbox}.");
            }
            else if (!Path.IsPathRooted(options.OutboxPath))
            {
                failures.Add("Email:OutboxPath must be an absolute path.");
            }
            else if (EmailOutboxLocation.IsInsideGitWorkingTree(options.OutboxPath))
            {
                failures.Add("Email:OutboxPath must not be inside a Git working tree.");
            }
        }

        if (options.RegistrationTokenTtlHours is < 1 or > 168)
        {
            failures.Add("Email:RegistrationTokenTtlHours must be between 1 and 168.");
        }

        if (options.EmailChangeTokenTtlHours is < 1 or > 72)
        {
            failures.Add("Email:EmailChangeTokenTtlHours must be between 1 and 72.");
        }

        if (options.AdminActivationTokenTtlHours is < 1 or > 168)
        {
            failures.Add("Email:AdminActivationTokenTtlHours must be between 1 and 168.");
        }

        if (options.ResendCooldownSeconds is < 0 or > 3600)
        {
            failures.Add("Email:ResendCooldownSeconds must be between 0 and 3600.");
        }

        if (options.ResendDailyCap is < 1 or > 100)
        {
            failures.Add("Email:ResendDailyCap must be between 1 and 100.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
