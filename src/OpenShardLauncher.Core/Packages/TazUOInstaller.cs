using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Packages;

// Installs or updates the TazUO launcher in <install folder>/<TazUO.InstallFolder> from a verified package, records
// its version and sets up the shard's profiles. Starting it is IGameLauncher's job.
//
// The zip is downloaded into the install folder's cache and unpacked (SafeZip) next to it first, so a bad or oversized
// zip never touches the installed TazUO. Only then are its files moved over the installed ones. Files the zip doesn't
// have, such as the players' profiles, are kept.
public sealed class TazUOInstaller(
    IUpdateServer server,
    FeedStateStore feedState,
    LauncherOptions options,
    WorkflowOptions workflowOptions,
    TimeProvider time,
    ILogger<TazUOInstaller> logger)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // False when it couldn't be installed (logged); the game files still update. Throws only when cancelled.
    public async Task<bool> InstallAsync(
        PackageEntry package, InstallFolder folder, IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        if (!TryGetTarget(folder, out var target))
        {
            return false;
        }

        var zipPath = Path.Combine(folder.PackagesFolder, package.File);
        var staging = Path.Combine(folder.PackagesFolder, "staging");
        try
        {
            logger.LogInformation("Installing TazUO {Version} into {Folder}", package.Version, target);
            progress?.Report(new UpdateProgress(UpdatePhase.InstallingTazUO, 0, 1, 0, package.Size));
            var bytes = PackageProgress.For(progress, UpdatePhase.InstallingTazUO, package.Size, time);
            await server.DownloadPackageAsync(package, zipPath, bytes, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new UpdateProgress(UpdatePhase.InstallingTazUO, 0, 1));
            DeleteFolder(staging);
            SafeZip.ExtractToDirectory(zipPath, staging, workflowOptions.PackageLimits);
            MoveContents(staging, target);
            MakeExecutable(ExecutablePath(target));

            feedState.SetInstalledVersion(PackageRole.TazUO, package.Version);
            logger.LogInformation("Installed TazUO {Version}", package.Version);
            progress?.Report(new UpdateProgress(UpdatePhase.InstallingTazUO, 1, 1));
            TryDelete(zipPath);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A download that failed keeps its partial file for next time; a zip that was refused is deleted.
            logger.LogError(e, "Could not install TazUO {Version}", package.Version);
            if (e is InvalidDataException)
            {
                TryDelete(zipPath);
            }

            return false;
        }
        finally
        {
            TryDeleteFolder(staging);
        }
    }

    // Writes the configured profiles the TazUO launcher doesn't have yet. Problems are logged only.
    public void EnsureProfiles(InstallFolder folder)
    {
        if (!TryGetTarget(folder, out var target) || !Directory.Exists(target))
        {
            return;
        }

        foreach (var profile in options.TazUO.Profiles)
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

    private bool TryGetTarget(InstallFolder folder, out string target)
    {
        if (PathContainment.TryResolve(folder.Root, options.TazUO.InstallFolder, out var resolved)
            && !InstallFolder.IsReserved(Path.GetRelativePath(folder.Root, resolved)))
        {
            target = resolved;
            return true;
        }

        logger.LogError("TazUO.InstallFolder '{Folder}' in launcher.json isn't inside the install folder", options.TazUO.InstallFolder);
        target = "";
        return false;
    }

    private string ExecutablePath(string target) =>
        Path.Combine(target, options.TazUO.ExecutableName + (OperatingSystem.IsWindows() ? ".exe" : ""));

    // Same drive (both under the install folder), so every file is a rename.
    private static void MoveContents(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Move(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    internal static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }

        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private void TryDeleteFolder(string path)
    {
        try
        {
            DeleteFolder(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not remove {Folder}", path);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not remove {File}", path);
        }
    }
}
