using System.Security.Cryptography;
using System.Text;

namespace NIBRAS.API.Services;

/// <summary>
/// Creates and hashes the email verification tokens. The plaintext token exists only in the
/// memory of the caller that has to deliver it and in the activation link; the database
/// stores its SHA-256 hash, so a leaked database row cannot be replayed against the
/// verification endpoint.
/// </summary>
internal static class VerificationTokenCodec
{
    /// <summary>32 bytes = 256 bits of entropy, encoded as a 43 character base64url token.</summary>
    private const int TokenBytes = 32;

    /// <summary>
    /// Creates a new token from a cryptographically secure random source.
    /// </summary>
    public static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// The value stored in the database: the SHA-256 hash of the token. The comparison is a
    /// database equality on a 32-byte hash, so the plaintext never has to be stored.
    /// </summary>
    public static byte[] HashToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
