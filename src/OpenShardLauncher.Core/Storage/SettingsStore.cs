using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Core.Storage;

// settings.json in the launcher data folder.
public sealed class SettingsStore(LauncherDataFolder dataFolder, ILogger<SettingsStore> logger)
{
    // Defaults when the file is missing or can't be read. A corrupt file is kept as settings.json.corrupt so the next
    // save doesn't silently destroy what the player had.
    public UserSettings Load()
    {
        var path = dataFolder.SettingsFile;
        if (!File.Exists(path))
        {
            logger.LogInformation("No settings file at {Path}; using defaults", path);
            return new UserSettings();
        }

        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.UserSettings)
                ?? throw new JsonException("settings.json is null");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(e, "Could not read settings file {Path}; using defaults", path);
            TryKeepCorruptCopy(path);
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings) =>
        AtomicFile.WriteAllText(dataFolder.SettingsFile, JsonSerializer.Serialize(settings, CoreJsonContext.Default.UserSettings));

    private void TryKeepCorruptCopy(string path)
    {
        try
        {
            File.Copy(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "Could not keep a copy of the corrupt settings file");
        }
    }
}
