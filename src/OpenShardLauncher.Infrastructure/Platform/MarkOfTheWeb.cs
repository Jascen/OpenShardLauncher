namespace OpenShardLauncher.Infrastructure.Platform;

// Whether a file on NTFS carries the Mark of the Web (a Zone.Identifier stream a browser adds to downloads). The
// launcher only logs it at startup: SmartScreen/Smart App Control decide on it, and self-update never copies the
// running exe so the mark isn't carried into the update. It is never stripped.
public static class MarkOfTheWeb
{
    public static bool IsMarked(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
