using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NIBRAS.API.Services;

/// <summary>
/// Deployment configuration that must be explicit outside Development. These checks are about
/// availability and privilege boundaries, so a missing value fails the host instead of silently
/// falling back to a permissive default.
/// </summary>
public static class ProductionConfigurationGuard
{
    public static void Validate(
        IHostEnvironment environment,
        IConfiguration configuration,
        bool bootstrapInvocation = false)
    {
        if (environment.IsDevelopment() && !bootstrapInvocation)
        {
            return;
        }

        ValidateAllowedHosts(configuration["AllowedHosts"]);
        ValidateDevelopmentOnlySeed(configuration);
        ValidateBootstrapCredentials(configuration, bootstrapInvocation);
    }

    /// <summary>
    /// A production host must name the hosts it serves. The repository ships loopback names for
    /// local development; wildcard values are a local convenience only and are refused here. The
    /// host-filtering middleware then enforces the configured allow-list.
    /// </summary>
    public static void ValidateAllowedHosts(string? allowedHosts)
    {
        var hosts = (allowedHosts ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (hosts.Length == 0)
        {
            throw new InvalidOperationException(
                "AllowedHosts must list the deployment host names outside Development; an empty value is not allowed.");
        }

        if (hosts.Any(host => host.Contains('*')))
        {
            throw new InvalidOperationException(
                "AllowedHosts must list explicit deployment host names outside Development; wildcards are not allowed.");
        }
    }

    /// <summary>
    /// The Development SuperAdmin seeder is never a production provisioning path. Refuse a
    /// production-like start that still carries its credentials, so a copied development
    /// configuration cannot look like an authorized production bootstrap.
    /// </summary>
    public static void ValidateDevelopmentOnlySeed(IConfiguration configuration)
    {
        var email = configuration["Seed:SuperAdminEmail"];
        var password = configuration["Seed:SuperAdminPassword"];

        if (!string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Seed:SuperAdminEmail and Seed:SuperAdminPassword are Development-only. " +
                "Production SuperAdmin provisioning requires a separate, explicitly authorized one-time procedure; " +
                "the host will not create a privileged account from startup configuration.");
        }
    }

    /// <summary>
    /// The bootstrap credentials of <see cref="SuperAdminBootstrap"/> are honored only by the
    /// explicit out-of-band invocation. A normal application start — Development included —
    /// must not carry them, so a forgotten secret can never sit in the resolved configuration
    /// of a serving host.
    /// </summary>
    public static void ValidateBootstrapCredentials(IConfiguration configuration, bool bootstrapInvocation)
    {
        var email = configuration[SuperAdminBootstrap.EmailKey];
        var password = configuration[SuperAdminBootstrap.PasswordKey];

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (!bootstrapInvocation)
        {
            throw new InvalidOperationException(
                $"\"{SuperAdminBootstrap.EmailKey}\" and \"{SuperAdminBootstrap.PasswordKey}\" are honored only by the " +
                $"explicit one-time bootstrap invocation (\"{SuperAdminBootstrap.InvocationArgument}\"). " +
                "A normal application start must not carry bootstrap credentials; remove the values and, " +
                "when provisioning is intended, invoke the bootstrap command instead.");
        }
    }
}
