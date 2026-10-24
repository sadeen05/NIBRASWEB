using Microsoft.Extensions.Configuration;

namespace NIBRAS.API.Services;

/// <summary>
/// Reads and validates the JWT settings at startup. The signing key, the issuer, the audience
/// and the lifetime are part of the security boundary, so a missing or malformed value fails the
/// host immediately instead of producing tokens that can never validate or a session that lives
/// for an unintended period.
/// </summary>
public static class JwtConfiguration
{
    public static (byte[] Key, string Issuer, string Audience) Read(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "JWT signing key is missing. Configure Jwt:Key in User Secrets or environment variables.");
        }

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("JWT signing key must be valid Base64.", ex);
        }

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("JWT signing key must decode to at least 32 bytes.");
        }

        var issuer = configuration["Jwt:Issuer"];
        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException("JWT issuer is missing. Configure Jwt:Issuer.");
        }

        var audience = configuration["Jwt:Audience"];
        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("JWT audience is missing. Configure Jwt:Audience.");
        }

        if (configuration.GetValue<int?>("Jwt:ExpiryMinutes") is not > 0)
        {
            throw new InvalidOperationException(
                "JWT expiry must be a positive number of minutes. Configure Jwt:ExpiryMinutes.");
        }

        return (keyBytes, issuer, audience);
    }
}
