namespace NIBRAS.API.Services;

/// <summary>
/// Resolves the private document root and checks containment. The same rules are used by the
/// startup validator and by the storage service, so a path that the host accepts cannot be
/// interpreted differently on the first upload.
/// </summary>
public static class DocumentStoragePath
{
    /// <summary>
    /// Resolves the configured root. An empty value keeps the Development fallback below
    /// <c>App_Data</c>; a relative value is resolved against the content root.
    /// </summary>
    public static string ResolveRoot(string contentRootPath, string? configuredRoot)
    {
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(contentRootPath, "App_Data", "land-documents")
            : Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(contentRootPath, configuredRoot);

        return Path.GetFullPath(root);
    }

    /// <summary>Resolves the public web root below the content root.</summary>
    public static string ResolveWebRoot(string contentRootPath) =>
        Path.GetFullPath(Path.Combine(contentRootPath, "wwwroot"));

    /// <summary>
    /// Whether <paramref name="path"/> is the directory itself or lies below it. The comparison
    /// is case-insensitive, which is conservative on case-sensitive file systems.
    /// </summary>
    public static bool IsInside(string path, string directory)
    {
        var prefix = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }
}
