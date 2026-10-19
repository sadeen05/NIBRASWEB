namespace NIBRAS.API.Services;

/// <summary>
/// Builds the verification links of every verification mail. The token always travels in the
/// <b>URL fragment</b>, which no browser ever sends to a server, so it cannot appear in an
/// access log, a proxy log or a referrer header. The path is a fixed local path that is
/// validated at startup, so a link can never be pointed somewhere else.
/// </summary>
public static class VerificationLinkBuilder
{
    /// <summary>
    /// Builds <c>{baseUrl}{path}#token=…</c> for the given token.
    /// </summary>
    /// <param name="baseUrl">The base address of the frontend.</param>
    /// <param name="path">The local path of the verification page.</param>
    /// <param name="token">The plaintext token; it is escaped and never logged.</param>
    public static string Build(string baseUrl, string path, string token)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var localPath = path.StartsWith('/') ? path : "/" + path;

        // The fragment is never transmitted to a server, so the token cannot end up in an
        // access log, a proxy log or a referrer header.
        return $"{trimmedBase}{localPath}#token={Uri.EscapeDataString(token)}";
    }
}
