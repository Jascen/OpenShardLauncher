namespace OpenShardLauncher.Infrastructure.Platform;

// Whether the launcher can write to a folder: creates it if needed, then writes and deletes a probe file. Used for the
// install folder in Settings and, before offering a self-update, for the launcher's own folder (Program Files, or a
// read-only App Translocation copy on macOS, fail here).
public static class FolderProbe
{
    public static bool IsWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".openshardlauncher-probe-{Guid.NewGuid():N}");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
