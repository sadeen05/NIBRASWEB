namespace NIBRAS.API.Services;

/// <summary>
/// Location rules for the development outbox. The outbox holds real activation links, so it
/// must never live inside a Git working tree (it would be committed by accident) and it is
/// created with owner-only access where the platform supports it.
/// </summary>
internal static class EmailOutboxLocation
{
    /// <summary>
    /// Whether the path lies inside a Git working tree. An unresolvable path is treated as
    /// unsafe, so the check fails closed.
    /// </summary>
    public static bool IsInsideGitWorkingTree(string path)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            while (directory is not null)
            {
                var marker = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return true;
                }

                directory = directory.Parent;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    /// <summary>Restricts a directory to the owner where the platform supports it.</summary>
    public static void RestrictDirectoryToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // A directory below the user's temporary path inherits that user's ACL; the
            // platform offers no portable way to narrow it further.
            return;
        }

        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // Best effort: the outbox is a development sink, never a production path.
        }
    }

    /// <summary>Restricts a file to the owner where the platform supports it.</summary>
    public static void RestrictFileToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort, as above.
        }
    }
}
