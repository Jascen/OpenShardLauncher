using System.Diagnostics.CodeAnalysis;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Storage;

// The game install folder. Every path the feed writes or deletes goes through PathFor.
public sealed class InstallFolder
{
    // The launcher's own cache (hash cache, partial downloads). Safe to delete; on the install folder's drive so the
    // final move of a download is atomic.
    public const string CacheFolderName = ".openshardlauncher-cache";

    // The player's ignore list. At the top level because players edit it.
    public const string IgnoreFileName = IgnoreRules.PlayerFileName;

    public InstallFolder(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Root { get; }

    public string CacheFolder => Path.Combine(Root, CacheFolderName);

    public string DownloadsFolder => Path.Combine(CacheFolder, "downloads");

    // Package zips (TazUO) while they download, and what they unpack to before it is moved into place.
    public string PackagesFolder => Path.Combine(CacheFolder, "packages");

    public string IgnoreFilePath => Path.Combine(Root, IgnoreFileName);

    // The install folder for these settings: the player's choice, or the default subfolder next to the launcher.
    public static string ResolvePath(UserSettings settings, LauncherOptions options, string launcherFolder) =>
        Path.GetFullPath(settings.InstallPath ?? Path.Combine(launcherFolder, options.DefaultInstallFolder));

    // The install folder can't be the launcher's folder or contain it (self-update copies over the launcher folder,
    // and the launcher's data folder would become game files).
    public static bool IsAllowedLocation(string installPath, string launcherFolder)
    {
        var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath)) + Path.DirectorySeparatorChar;
        var launcher = Path.TrimEndingDirectorySeparator(Path.GetFullPath(launcherFolder)) + Path.DirectorySeparatorChar;
        return !launcher.StartsWith(install, PathComparison);
    }

    // True for names the feed may never write or delete: anything in the cache folder, and the top-level ignore list.
    // Expects a name already resolved relative to the root ('/' or '\' separators).
    public static bool IsReserved(string relativeName)
    {
        var segments = relativeName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        return string.Equals(segments[0], CacheFolderName, StringComparison.OrdinalIgnoreCase)
            || (segments.Length == 1 && string.Equals(segments[0], IgnoreFileName, StringComparison.OrdinalIgnoreCase));
    }

    // The full path for a feed name, or false when it resolves outside the root or to a reserved name.
    // The reserved check runs on where the name resolves, so "a/../.openshardlauncher-cache/x" is caught too.
    public bool TryGetPathFor(string name, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        if (!PathContainment.TryResolve(Root, name, out var resolved) || IsReserved(Path.GetRelativePath(Root, resolved)))
        {
            return false;
        }

        fullPath = resolved;
        return true;
    }

    public string PathFor(string name) =>
        TryGetPathFor(name, out var fullPath)
            ? fullPath
            : throw new ArgumentException($"'{name}' is outside the install folder or reserved.", nameof(name));

    // The player's ignore list; none when the file doesn't exist.
    public IgnoreRules LoadIgnoreRules() =>
        File.Exists(IgnoreFilePath) ? IgnoreRules.Parse(File.ReadAllText(IgnoreFilePath)) : IgnoreRules.None;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
