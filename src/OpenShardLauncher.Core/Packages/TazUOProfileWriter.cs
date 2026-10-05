using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Packages;

// Sets up the shard's profiles (Client.TazUOProfiles) in the TazUO launcher, when that is the client. Each profile has
// a well-known Id, so one the player already has (or changed) is left alone. Nothing happens without profiles, so a
// client that isn't the TazUO launcher just leaves the list empty.
public sealed class TazUOProfileWriter(LauncherOptions options, ILogger<TazUOProfileWriter> logger)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Writes the configured profiles the TazUO launcher doesn't have yet. Problems are logged only.
    public void EnsureProfiles(InstallFolder folder)
    {
        if (options.Client.TazUOProfiles.Count == 0
            || !folder.TryGetClientFolder(options.Client, out var target)
            || !Directory.Exists(target))
        {
            return;
        }

        foreach (var profile in options.Client.TazUOProfiles)
        {
            try
            {
                CreateProfileIfMissing(folder, target, profile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogWarning(e, "Could not create the TazUO profile {Profile}", profile.Id);
            }
        }
    }

    // The TazUO launcher reads two files per profile: Profiles/<id>.json and Profiles/Settings/<id>.json.
    private void CreateProfileIfMissing(InstallFolder folder, string target, TazUOProfile profile)
    {
        var profiles = Path.Combine(target, "Profiles");
        if (!PathContainment.TryResolve(profiles, profile.Id + ".json", out var profilePath)
            || Path.GetDirectoryName(profilePath) != profiles)
        {
            logger.LogError("The TazUO profile id '{Profile}' in launcher.json isn't a plain file name; skipping it", profile.Id);
            return;
        }

        if (File.Exists(profilePath))
        {
            return;
        }

        var settingsFolder = Path.Combine(profiles, "Settings");
        Directory.CreateDirectory(settingsFolder);
        var clientSettings = new JsonObject
        {
            ["ip"] = profile.Ip,
            ["port"] = profile.Port,
            ["ultimaonlinedirectory"] = folder.Root, // The game files this launcher downloads
            ["clientversion"] = profile.ClientVersion,
            ["lastservernum"] = 1,
        };
        File.WriteAllText(Path.Combine(settingsFolder, profile.Id + ".json"), clientSettings.ToJsonString(Indented));

        var launcherProfile = new JsonObject
        {
            ["Name"] = profile.Name,
            ["SettingsFile"] = profile.Id,
            ["FileName"] = profile.Id,
            ["LastCharacterName"] = "",
            ["AdditionalArgs"] = "",
        };
        File.WriteAllText(profilePath, launcherProfile.ToJsonString(Indented));
        logger.LogInformation("Created the TazUO profile {Profile}", profile.Name);
    }
}
