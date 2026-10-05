using Microsoft.Extensions.Logging;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Core.Packages;

// What the package manifest offers this install. Manifest is null when there is none (nothing published, or it
// couldn't be trusted or fetched; Warnings says which).
public sealed record PackageUpdates
{
    public static PackageUpdates None { get; } = new();

    public PackageManifest? Manifest { get; init; }

    public FeedTrust? Trust { get; init; }

    // The client package to install: the client is missing or older than this. Null when it is up to date (or when the
    // feed has no client package, e.g. because the client is part of the game files).
    public PackageEntry? Client { get; init; }

    public IReadOnlyList<PackageWarning> Warnings { get; init; } = [];
}

// Reads the verified package manifest and decides whether the game client needs installing or updating. Package problems only
// produce warnings: game files update whatever happens here.
//
// Downgrade protection: the highest manifest version seen per role is remembered (FeedStateStore), and a validly signed
// manifest offering an older one is refused. FeedVerifier only does the equivalent for files.json.
public sealed class PackageCheckService(
    IUpdateServer server,
    IGameLauncher game,
    FeedStateStore feedState,
    LauncherOptions options,
    PlatformInfo platform,
    ILogger<PackageCheckService> logger)
{
    public async Task<PackageUpdates> CheckAsync(InstallFolder folder, CancellationToken cancellationToken)
    {
        if (options.TrustedPublicKeys.Count == 0 && !options.AllowUnsignedFeed)
        {
            return new PackageUpdates { Warnings = [PackageWarning.PackagesNotConfigured] };
        }

        var result = await server.GetPackageManifestAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                // The feed has no packages; nothing to warn about.
                UpdateError.NothingPublished => PackageUpdates.None,
                UpdateError.FeedUntrusted or UpdateError.UnsignedFeedNotDefaultServer or UpdateError.UnsignedFeedInsecure =>
                    Warn(PackageWarning.ManifestUntrusted),
                UpdateError.FeedUpdating => Warn(PackageWarning.ManifestUpdating),
                _ => Warn(PackageWarning.ManifestUnreachable),
            };
        }

        var manifest = result.Document!;
        var updates = new PackageUpdates { Manifest = manifest, Trust = result.Trust };
        if (!options.Client.Enabled)
        {
            return updates;
        }

        if (platform.Rid is not { } rid || manifest.Find(PackageRole.Client, rid) is not { } package)
        {
            logger.LogInformation("The manifest has no client package for {Platform}", platform.Rid ?? "this platform");
            return updates;
        }

        if (!AcceptVersion(PackageRole.Client, package))
        {
            return updates with { Warnings = [PackageWarning.ManifestUntrusted] };
        }

        var installed = feedState.GetInstalledVersion(PackageRole.Client);
        if (!game.IsInstalled(folder.Root))
        {
            logger.LogInformation("The client is not installed; version {Version} is available", package.Version);
            return updates with { Client = package };
        }

        // Installed, but by something that didn't record a version: offer the package so the install is known again.
        if (installed is null || !Version.TryParse(installed, out var current) || PackageVersions.IsNewer(package.Version, current))
        {
            logger.LogInformation("The client {Installed} can be updated to {Version}", installed ?? "(unknown version)", package.Version);
            return updates with { Client = package };
        }

        return updates;
    }

    // False for a package older than the newest one a manifest has offered for its role before (a rollback); otherwise
    // remembers its version when it is the newest so far.
    public bool AcceptVersion(string role, PackageEntry package)
    {
        var offered = PackageVersions.Normalize(Version.Parse(package.Version));
        if (feedState.GetHighestManifestVersion(role) is { } stored && Version.TryParse(stored, out var parsed))
        {
            var highest = PackageVersions.Normalize(parsed);
            if (offered < highest)
            {
                logger.LogError(
                    "The manifest offers {Role} {Version}, older than {Highest} offered before; refusing a rollback",
                    role, package.Version, stored);
                return false;
            }

            if (offered == highest)
            {
                return true;
            }
        }

        feedState.SetHighestManifestVersion(role, package.Version);
        return true;
    }

    private PackageUpdates Warn(PackageWarning warning)
    {
        logger.LogWarning("Packages are unavailable: {Warning}; game files still update", warning);
        return new PackageUpdates { Warnings = [warning] };
    }
}
