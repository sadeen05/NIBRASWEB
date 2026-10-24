using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace NIBRAS.API.Services;

/// <summary>
/// Configuration of the reverse-proxy trust boundary. Forwarded headers are accepted only when
/// the feature is explicitly enabled and at least one proxy address or network is explicitly
/// listed. There is no wildcard, default-proxy or blanket-trust mode.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>Whether forwarded headers are accepted. Disabled by default.</summary>
    public bool Enabled { get; set; }

    /// <summary>Explicit proxy IP addresses, for example <c>10.0.0.10</c>.</summary>
    public string[] KnownProxies { get; set; } = Array.Empty<string>();

    /// <summary>Explicit proxy networks in CIDR form, for example <c>10.0.0.0/8</c>.</summary>
    public string[] KnownNetworks { get; set; } = Array.Empty<string>();

    /// <summary>Number of trusted proxy hops to process. Defaults to one.</summary>
    public int ForwardLimit { get; set; } = 1;
}

/// <summary>
/// Rejects an unsafe reverse-proxy configuration at startup: enabling forwarded headers without
/// an explicit trust list, wildcard entries, any-address proxies, network prefixes covering the
/// whole address space, malformed values and an invalid hop count.
/// </summary>
public sealed class ReverseProxyOptionsValidator : IValidateOptions<ReverseProxyOptions>
{
    private const int MaxForwardLimit = 10;

    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        var failures = new List<string>();

        if (options.ForwardLimit is < 1 or > MaxForwardLimit)
        {
            failures.Add($"ReverseProxy:ForwardLimit must be between 1 and {MaxForwardLimit}.");
        }

        if (!options.Enabled)
        {
            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        var proxies = options.KnownProxies ?? Array.Empty<string>();
        var networks = options.KnownNetworks ?? Array.Empty<string>();

        if (proxies.Length == 0 && networks.Length == 0)
        {
            failures.Add(
                "ReverseProxy:KnownProxies or ReverseProxy:KnownNetworks must explicitly list the trusted proxy when ReverseProxy:Enabled is true.");
        }

        foreach (var value in proxies)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !IPAddress.TryParse(value.Trim(), out var address) ||
                IsAnyAddress(address))
            {
                failures.Add(
                    "ReverseProxy:KnownProxies must contain explicit unicast IP addresses; wildcard, any-address, CIDR and empty entries are not allowed.");
                break;
            }
        }

        foreach (var value in networks)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !IPNetwork.TryParse(value.Trim(), out var network) ||
                network.PrefixLength == 0 ||
                IsAnyAddress(network.BaseAddress))
            {
                failures.Add(
                    "ReverseProxy:KnownNetworks must contain explicit CIDR networks with a non-zero prefix; wildcard, any-address, malformed and empty entries are not allowed.");
                break;
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAnyAddress(IPAddress? address) =>
        address is not null &&
        (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));
}

/// <summary>
/// Builds the middleware options from a validated configuration. The framework defaults are
/// cleared first: only the configured proxies and networks are trusted, never the implicit
/// loopback defaults.
/// </summary>
public static class ReverseProxyConfiguration
{
    public static ForwardedHeadersOptions Create(ReverseProxyOptions options)
    {
        var forwardedHeaders = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = options.ForwardLimit,
        };

        forwardedHeaders.KnownProxies.Clear();
        forwardedHeaders.KnownIPNetworks.Clear();

        foreach (var proxy in options.KnownProxies ?? Array.Empty<string>())
        {
            forwardedHeaders.KnownProxies.Add(IPAddress.Parse(proxy.Trim()));
        }

        foreach (var network in options.KnownNetworks ?? Array.Empty<string>())
        {
            forwardedHeaders.KnownIPNetworks.Add(IPNetwork.Parse(network.Trim()));
        }

        return forwardedHeaders;
    }
}
