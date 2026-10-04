using Microsoft.Extensions.Logging;

namespace OpenShardLauncher.Core.Storage;

// Where settings, logs, feed state and the self-update marker live. Portable first: <launcher folder>/.openshardlauncher/,
// so each launcher folder is self-contained. Only when that isn't writable (Program Files, macOS App Translocation)
// is the per-user AppData folder used. The folder names are a stable contract.
public sealed class LauncherDataFolder
{
    public const string PortableFolderName = ".openshardlauncher";
    public const string SettingsFileName = "settings.json";
    public const string UpdateResultFileName = "update-result.json";

    private LauncherDataFolder(string path, bool isPortable)
    {
        Path = path;
        IsPortable = isPortable;
    }

    public string Path { get; }

    public bool IsPortable { get; }

    public string SettingsFile => System.IO.Path.Combine(Path, SettingsFileName);

    public string FeedStateFile => System.IO.Path.Combine(Path, "feed-state.json");

    public string UpdateResultFile => System.IO.Path.Combine(Path, UpdateResultFileName);

    public string LogsFolder => System.IO.Path.Combine(Path, "logs");

    // Order: an existing portable settings.json, then portable if writable, then AppData.
    // appDataRoot and isWritable are for tests; by default they are the platform's per-user folder and a real probe.
    public static LauncherDataFolder Resolve(
        string launcherFolder,
        string appDataFolderName,
        ILogger logger,
        string? appDataRoot = null,
        Func<string, bool>? isWritable = null)
    {
        isWritable ??= ProbeWritable;
        var portable = System.IO.Path.Combine(System.IO.Path.GetFullPath(launcherFolder), PortableFolderName);

        if (File.Exists(System.IO.Path.Combine(portable, SettingsFileName)))
        {
            logger.LogInformation("Using the portable launcher data folder {Path} (existing settings)", portable);
            return new LauncherDataFolder(portable, isPortable: true);
        }

        if (isWritable(portable))
        {
            logger.LogInformation("Using the portable launcher data folder {Path}", portable);
            return new LauncherDataFolder(portable, isPortable: true);
        }

        var appData = System.IO.Path.Combine(appDataRoot ?? DefaultAppDataRoot(), appDataFolderName);
        Directory.CreateDirectory(appData);
        logger.LogInformation("The launcher folder isn't writable; using {Path} for launcher data", appData);
        return new LauncherDataFolder(appData, isPortable: false);
    }

    // %AppData% on Windows, ~/Library/Application Support on macOS, $XDG_CONFIG_HOME (~/.config) on Linux.
    private static string DefaultAppDataRoot() =>
        Environment.GetFolderPath(OperatingSystem.IsMacOS()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ApplicationData);

    // Creates the folder if needed and writes and deletes a probe file in it.
    private static bool ProbeWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = System.IO.Path.Combine(folder, $".probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
