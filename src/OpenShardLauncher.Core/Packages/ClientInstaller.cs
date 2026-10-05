using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Core.Packages;

// Installs or updates the game client in <install folder>/<Client.InstallFolder> from a verified `client` package and
// records its version. Starting it is IGameLauncher's job.
//
// The zip is downloaded into the install folder's cache and unpacked (SafeZip) next to it first, so a bad or oversized
// zip never touches the installed client. Only then are its files moved over the installed ones. Files the zip doesn't
// have, such as the players' profiles or settings, are kept.
public sealed class ClientInstaller(
    IUpdateServer server,
    FeedStateStore feedState,
    LauncherOptions options,
    WorkflowOptions workflowOptions,
    TimeProvider time,
    ILogger<ClientInstaller> logger)
{
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
            logger.LogInformation("Installing the client {Version} into {Folder}", package.Version, target);
            progress?.Report(new UpdateProgress(UpdatePhase.InstallingClient, 0, 1, 0, package.Size));
            var bytes = PackageProgress.For(progress, UpdatePhase.InstallingClient, package.Size, time);
            await server.DownloadPackageAsync(package, zipPath, bytes, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new UpdateProgress(UpdatePhase.InstallingClient, 0, 1));
            DeleteFolder(staging);
            SafeZip.ExtractToDirectory(zipPath, staging, workflowOptions.PackageLimits);
            MoveContents(staging, target);
            MakeExecutable(InstallFolder.ClientExecutablePath(target, options.Client));

            feedState.SetInstalledVersion(PackageRole.Client, package.Version);
            logger.LogInformation("Installed the client {Version}", package.Version);
            progress?.Report(new UpdateProgress(UpdatePhase.InstallingClient, 1, 1));
            TryDelete(zipPath);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A download that failed keeps its partial file for next time; a zip that was refused is deleted.
            logger.LogError(e, "Could not install the client {Version}", package.Version);
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

    private bool TryGetTarget(InstallFolder folder, out string target)
    {
        if (folder.TryGetClientFolder(options.Client, out target))
        {
            return true;
        }

        logger.LogError("Client.InstallFolder '{Folder}' in launcher.json isn't inside the install folder", options.Client.InstallFolder);
        return false;
    }

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
