using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Shared.Feed;
using OpenShardLauncher.Shared.Files;

namespace OpenShardLauncher.Core.Packages;

// A newer launcher the current server offers for this platform. Server is where it was found: an offer from another
// server is never applied.
public sealed record LauncherUpdateOffer(PackageEntry Package, FeedTrust Trust, Uri Server);

public enum SelfUpdateError
{
    NotOffered, // Nothing to update to (any more), or the server changed since
    DownloadFailed,
    VerificationFailed, // The package no longer matches the signed manifest, or the manifest can't be fetched again
    PackageInvalid, // Unsafe zip, or no exe of this launcher's name in it
    HandOffFailed, // The new version couldn't be started
}

// HandedOff: the new version is running as the applier and this launcher should exit now.
public sealed record SelfUpdateResult(bool HandedOff, SelfUpdateError? Error)
{
    public static SelfUpdateResult Success { get; } = new(true, null);

    public static SelfUpdateResult Failure(SelfUpdateError error) => new(false, error);
}

// The launcher's own updates. Refresh reads the verified manifest and offers the package for this platform only when
// it is strictly newer than this launcher (downgrade protection, plus the highest version seen for the role). A dev
// build without a numeric version is never offered one. "Not now" hides the offer until the launcher restarts.
//
// Updating: download into .temp/ next to the exe, check the package against a freshly verified manifest and hash it
// again, unpack it (SafeZip) to .temp/staging/, write the result marker and hand off to the staged exe, which applies
// itself (ISelfUpdater, LauncherSwap). Nothing is ever extracted to %TEMP%.
public sealed class LauncherSelfUpdateService(
    IUpdateServer server,
    ISelfUpdater selfUpdater,
    PackageCheckService packages,
    ServerEndpoint endpoint,
    InstalledLauncher launcher,
    PlatformInfo platform,
    LauncherOptions options,
    LauncherDataFolder dataFolder,
    WorkflowOptions workflowOptions,
    TimeProvider time,
    ILogger<LauncherSelfUpdateService> logger)
{
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private readonly Lock _lock = new();
    private LauncherUpdateOffer? _offer;
    private bool _dismissed;

    // Raised on the thread that changed the offer.
    public event EventHandler? OfferChanged;

    // What the banner shows; null when there's nothing to offer or the player said "Not now".
    public LauncherUpdateOffer? Offer
    {
        get
        {
            lock (_lock)
            {
                return _dismissed ? null : _offer;
            }
        }
    }

    // False when the launcher has no keys and doesn't allow unsigned feeds: it could never verify an update.
    public bool IsConfigured => options.TrustedPublicKeys.Count > 0 || options.AllowUnsignedFeed;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetOffer(await FindOfferAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _refreshing.Release();
        }
    }

    // "Not now": hidden until the launcher restarts.
    public void Dismiss()
    {
        lock (_lock)
        {
            _dismissed = true;
        }

        logger.LogInformation("The launcher update was put off until the next start");
        OfferChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<SelfUpdateResult> UpdateAsync(IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        if (Offer is not { } offer || offer.Server != endpoint.Current || platform.Rid is not { } rid)
        {
            return SelfUpdateResult.Failure(SelfUpdateError.NotOffered);
        }

        var package = offer.Package;
        var zipPath = Path.Combine(launcher.TempFolder, package.File);
        logger.LogInformation("Updating the launcher from {Current} to {Version}", launcher.Version, package.Version);
        progress?.Report(new UpdateProgress(UpdatePhase.UpdatingLauncher, 0, 1, 0, package.Size));
        try
        {
            Directory.CreateDirectory(launcher.TempFolder);
            await server.DownloadPackageAsync(package, zipPath, PackageProgress.For(progress, UpdatePhase.UpdatingLauncher, package.Size, time), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is UpdateException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not download launcher {Version}", package.Version);
            return SelfUpdateResult.Failure(SelfUpdateError.DownloadFailed);
        }

        progress?.Report(new UpdateProgress(UpdatePhase.UpdatingLauncher, 0, 1));
        if (!await IsStillOfferedAsync(package, rid, zipPath, cancellationToken).ConfigureAwait(false))
        {
            TryDelete(zipPath);
            return SelfUpdateResult.Failure(SelfUpdateError.VerificationFailed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Stage(zipPath) is { } stageError)
        {
            return SelfUpdateResult.Failure(stageError);
        }

        // From here on the hand-off isn't cancelled: the marker is written and the applier may already be starting.
        try
        {
            UpdateResultMarker.WriteExpected(dataFolder.UpdateResultFile, package.Version, launcher.Version?.ToString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not write the self-update marker {Path}", dataFolder.UpdateResultFile);
            return SelfUpdateResult.Failure(SelfUpdateError.HandOffFailed);
        }

        if (await selfUpdater.HandOffAsync(launcher.StagingFolder, package.Version, CancellationToken.None).ConfigureAwait(false))
        {
            logger.LogInformation("Handed off to launcher {Version}; exiting", package.Version);
            return SelfUpdateResult.Success;
        }

        TryDelete(dataFolder.UpdateResultFile);
        return SelfUpdateResult.Failure(SelfUpdateError.HandOffFailed);
    }

    // Removes .temp/ next to the exe at startup. Best effort: the applier may still be exiting, and anything locked is
    // retried on the next start.
    public void CleanUpTempFolder()
    {
        if (!Directory.Exists(launcher.TempFolder))
        {
            return;
        }

        try
        {
            Directory.Delete(launcher.TempFolder, recursive: true);
            logger.LogInformation("Removed the self-update folder {Folder}", launcher.TempFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogInformation("Could not remove {Folder} yet ({Reason}); trying again next start", launcher.TempFolder, e.Message);
        }
    }

    private async Task<LauncherUpdateOffer?> FindOfferAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        if (launcher.Version is not { } current)
        {
            logger.LogInformation("This launcher has no numeric version, so it is never offered updates");
            return null;
        }

        if (platform.Rid is not { } rid)
        {
            return null;
        }

        var feed = endpoint.Current;
        var result = await server.GetPackageManifestAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            if (result.Error != UpdateError.NothingPublished)
            {
                logger.LogWarning("Could not check for a launcher update on {Server}: {Error}", feed, result.Error);
            }

            return null;
        }

        if (result.Document!.Find(PackageRole.Launcher, rid) is not { } package)
        {
            return null;
        }

        if (!packages.AcceptVersion(PackageRole.Launcher, package))
        {
            return null;
        }

        if (!PackageVersions.IsNewer(package.Version, current))
        {
            logger.LogInformation("The launcher is up to date ({Current}; the server has {Version})", current, package.Version);
            return null;
        }

        if (endpoint.Current != feed)
        {
            return null; // The server changed while this was fetched; its own refresh follows
        }

        logger.LogInformation(
            "Launcher {Version} is available{Unsigned}", package.Version, result.Trust == FeedTrust.Unsigned ? " (unsigned)" : "");
        return new LauncherUpdateOffer(package, result.Trust, feed);
    }

    // Immediately before the hand-off: the manifest (verified again) must still offer exactly this package, and the
    // downloaded zip must still have its size and hash.
    private async Task<bool> IsStillOfferedAsync(PackageEntry package, string rid, string zipPath, CancellationToken cancellationToken)
    {
        var manifest = await server.GetPackageManifestAsync(cancellationToken).ConfigureAwait(false);
        if (!manifest.Succeeded || manifest.Document!.Find(PackageRole.Launcher, rid) != package)
        {
            logger.LogError("The manifest no longer offers launcher {Version} ({Error}); not updating", package.Version, manifest.Error);
            return false;
        }

        var file = new FileInfo(zipPath);
        if (!file.Exists || file.Length != package.Size || Sha256Hex.OfFile(zipPath) != package.Sha256)
        {
            logger.LogError("The downloaded launcher package {File} no longer matches the manifest; not updating", package.File);
            return false;
        }

        return true;
    }

    private SelfUpdateError? Stage(string zipPath)
    {
        try
        {
            if (Directory.Exists(launcher.StagingFolder))
            {
                Directory.Delete(launcher.StagingFolder, recursive: true);
            }

            SafeZip.ExtractToDirectory(zipPath, launcher.StagingFolder, workflowOptions.PackageLimits);
            var exe = Path.Combine(launcher.StagingFolder, launcher.ExeName);
            if (!File.Exists(exe))
            {
                logger.LogError("The launcher package has no {Exe}; not updating", launcher.ExeName);
                return SelfUpdateError.PackageInvalid;
            }

            ClientInstaller.MakeExecutable(exe);
            return null;
        }
        catch (InvalidDataException e)
        {
            logger.LogError(e, "The launcher package {File} can't be unpacked safely", Path.GetFileName(zipPath));
            TryDelete(zipPath);
            return SelfUpdateError.PackageInvalid;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogError(e, "Could not unpack the launcher package into {Folder}", launcher.StagingFolder);
            return SelfUpdateError.PackageInvalid;
        }
    }

    private void SetOffer(LauncherUpdateOffer? offer)
    {
        bool changed;
        lock (_lock)
        {
            changed = _offer != offer;
            _offer = offer;
        }

        if (changed)
        {
            OfferChanged?.Invoke(this, EventArgs.Empty);
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

// Package versions are 2–4 numeric parts. "1.2" and "1.2.0" are the same version (System.Version alone says 1.2.0 is
// newer).
public static class PackageVersions
{
    public static bool IsNewer(string offered, Version current) =>
        Version.TryParse(offered, out var version) && Normalize(version) > Normalize(current);

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
