namespace OpenShardLauncher.Core.Storage;

// Writes through a temp file in the same folder and then replaces the target, so a crash never leaves half a file.
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, contents);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
