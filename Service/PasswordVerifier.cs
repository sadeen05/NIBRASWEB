namespace NIBRAS.API.Services;

/// <summary>
/// Verifies a password against a stored BCrypt hash without ever throwing. A hash that is not
/// a valid BCrypt hash - the placeholder of the seeded system account, an empty value, or a
/// value written by an older tool - can never match a password, so it is a failed login and
/// never a server error.
/// </summary>
public static class PasswordVerifier
{
    /// <summary>
    /// Whether the password matches the stored hash. An unreadable or missing hash is a failed
    /// verification, not an error.
    /// </summary>
    public static bool TryVerify(string? password, string? hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            // An unusable hash (for example the placeholder of the seeded system account) can
            // never authenticate anybody, so it is treated as a wrong password.
            return false;
        }
    }
}
