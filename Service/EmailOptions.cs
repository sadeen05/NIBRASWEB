namespace NIBRAS.API.Services;

/// <summary>
/// Configuration of the email verification delivery and of the token lifetimes. Bound from
/// the "Email" configuration section only: no address, host, user or password is part of the
/// source, and the SMTP password is expected to come from user-secrets or an environment
/// variable. Nothing in this class is ever written to a log.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Name of the configuration section these options are bound from.</summary>
    public const string SectionName = "Email";

    /// <summary>Delivery through an SMTP server. The only mode allowed outside Development.</summary>
    public const string ModeSmtp = "Smtp";

    /// <summary>
    /// Delivery into a private per-run directory. Development only: the outbox is refused
    /// outside Development, and its path may not sit inside a Git working tree.
    /// </summary>
    public const string ModeFileOutbox = "FileOutbox";

    /// <summary>
    /// Delivery is switched off. Every send fails closed, so a host that is not configured for
    /// mail can never create an account that pretends to have been verified.
    /// </summary>
    public const string ModeDisabled = "Disabled";

    /// <summary>One of <see cref="ModeSmtp"/>, <see cref="ModeFileOutbox"/> or <see cref="ModeDisabled"/>.</summary>
    public string DeliveryMode { get; set; } = ModeDisabled;

    /// <summary>The sender address of the verification mails.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>SMTP host used when the delivery mode is <see cref="ModeSmtp"/>.</summary>
    public string SmtpHost { get; set; } = "";

    /// <summary>SMTP port used when the delivery mode is <see cref="ModeSmtp"/>.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>Whether TLS is negotiated with the SMTP server.</summary>
    public bool SmtpEnableSsl { get; set; } = true;

    /// <summary>Optional SMTP user. When set, a password is required as well.</summary>
    public string SmtpUser { get; set; } = "";

    /// <summary>SMTP password. Read from configuration or user-secrets; never logged, never returned.</summary>
    public string SmtpPassword { get; set; } = "";

    /// <summary>
    /// Absolute path of the development outbox directory. It must be outside any Git working
    /// tree and is created with owner-only access where the platform supports it.
    /// </summary>
    public string OutboxPath { get; set; } = "";

    /// <summary>
    /// Base address of the frontend, used to build the activation link. The link carries the
    /// token in the URL fragment, which no browser sends to a server. It is required whenever
    /// a delivery mode is configured, because a host that cannot build a link cannot deliver
    /// an invitation.
    /// </summary>
    public string FrontendBaseUrl { get; set; } = "";

    /// <summary>Path of the frontend activation page (the frontend increment adds the route).</summary>
    public string ActivationPath { get; set; } = "/activate-admin";

    /// <summary>
    /// Path of the frontend registration verification page. The verification link carries the
    /// token in the URL fragment, which no browser sends to a server.
    /// </summary>
    public string VerificationPath { get; set; } = "/verify-email";

    /// <summary>
    /// Path of the frontend address-change confirmation page. The link carries the token in the
    /// URL fragment, which no browser sends to a server.
    /// </summary>
    public string EmailChangePath { get; set; } = "/confirm-email-change";

    /// <summary>Lifetime of a registration verification token (hours).</summary>
    public int RegistrationTokenTtlHours { get; set; } = 24;

    /// <summary>Lifetime of an email-change verification token (hours).</summary>
    public int EmailChangeTokenTtlHours { get; set; } = 1;

    /// <summary>Lifetime of an admin activation token (hours).</summary>
    public int AdminActivationTokenTtlHours { get; set; } = 24;

    /// <summary>Minimum number of seconds between two resends for the same account.</summary>
    public int ResendCooldownSeconds { get; set; } = 60;

    /// <summary>Maximum number of verification mails sent for one account per day.</summary>
    public int ResendDailyCap { get; set; } = 10;

    public bool IsSmtp => string.Equals(DeliveryMode, ModeSmtp, StringComparison.OrdinalIgnoreCase);

    public bool IsFileOutbox => string.Equals(DeliveryMode, ModeFileOutbox, StringComparison.OrdinalIgnoreCase);

    public bool IsDisabled => string.Equals(DeliveryMode, ModeDisabled, StringComparison.OrdinalIgnoreCase);
}
