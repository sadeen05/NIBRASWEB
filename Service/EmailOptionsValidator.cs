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

        // Whenever a delivery mode is configured, the activation link has to be buildable.
        if (!options.IsDisabled)
        {
            if (string.IsNullOrWhiteSpace(options.FrontendBaseUrl))
            {
                failures.Add("Email:FrontendBaseUrl is required when a delivery mode is configured.");
            }
            else if (!Uri.TryCreate(options.FrontendBaseUrl, UriKind.Absolute, out var frontendUri) ||
                     (frontendUri.Scheme != Uri.UriSchemeHttp && frontendUri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add("Email:FrontendBaseUrl must be an absolute http or https address.");
            }
            else if (!_environment.IsDevelopment() && frontendUri.Scheme != Uri.UriSchemeHttps)
            {
                // The activation link carries a token, so it must never travel over an
                // unencrypted connection outside development.
                failures.Add("Email:FrontendBaseUrl must use https outside Development: the activation link carries a token.");
            }
        }

        // The activation path is a fixed local path: it may not carry a query, a fragment or a
        // scheme, so the link can never be pointed somewhere else.
        if (!string.IsNullOrWhiteSpace(options.ActivationPath))
        {
            var activationPath = options.ActivationPath;
            if (!activationPath.StartsWith('/') ||
                activationPath.StartsWith("//") ||
                activationPath.Contains('?') ||
                activationPath.Contains('#') ||
                activationPath.Contains("://", StringComparison.Ordinal) ||
                activationPath.Any(char.IsWhiteSpace))
            {
                failures.Add("Email:ActivationPath must be a local path that starts with a single '/' and carries no query, fragment or scheme.");
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
