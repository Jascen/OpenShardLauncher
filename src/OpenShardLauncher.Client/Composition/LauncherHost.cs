using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Logging;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Client.Composition;

// Builds the launcher's generic host. CreateEmptyApplicationBuilder adds no configuration sources, so environment
// variables, command-line arguments and appsettings.json can never override LauncherOptions.
public static class LauncherHost
{
    public static IHost Build(string launcherFolder)
    {
#if DEBUG
        const bool includeLocalOptions = true;
#else
        const bool includeLocalOptions = false;
#endif
        var options = LauncherOptionsLoader.Load(launcherFolder, includeLocalOptions);

        // Resolved before logging exists, because the logs live in it; which one was chosen is logged below.
        var dataFolder = LauncherDataFolder.Resolve(launcherFolder, options.AppDataFolderName, NullLogger.Instance);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "OpenShardLauncher",
            ContentRootPath = launcherFolder,
        });
        builder.Logging.AddLauncherFileLogging(dataFolder.LogsFolder);
        builder.Services.AddLauncher(options, dataFolder, new LauncherFolder(launcherFolder));

        var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LauncherHost));
        LogStartup(logger, options, dataFolder, host.Services.GetRequiredService<InstalledLauncher>());

        // What a finished (or abandoned) self-update left next to the exe. The applier may still be exiting.
        host.Services.GetRequiredService<LauncherSelfUpdateService>().CleanUpTempFolder();
        return host;
    }

    private static void LogStartup(ILogger logger, LauncherOptions options, LauncherDataFolder dataFolder, InstalledLauncher launcher)
    {
        logger.LogInformation(
            "{Title} launcher {Version} starting on {Platform}; update server {UpdateUrl}, {KeyCount} trusted keys",
            options.Title, LauncherVersion.Current, PlatformId.Current ?? "an unknown platform", options.UpdateUrl, options.TrustedPublicKeys.Count);

        if (dataFolder.IsPortable)
        {
            logger.LogInformation("Launcher data folder: {Path}", dataFolder.Path);
        }
        else
        {
            logger.LogWarning("The launcher folder isn't writable; launcher data is in {Path} and self-update is disabled", dataFolder.Path);
        }

        if (options.TrustedPublicKeys.Count == 0 && !options.AllowUnsignedFeed)
        {
            logger.LogError(
                "No trusted keys and AllowUnsignedFeed is off, so nothing can be verified and no update checks run. " +
                "Add a key from `publisher keygen` to launcher.json, or set AllowUnsignedFeed");
        }
        else if (options.AllowUnsignedFeed)
        {
            logger.LogWarning("Feed signatures are not required (AllowUnsignedFeed): unsigned feeds are accepted from {UpdateUrl} over https or loopback", options.UpdateUrl);
        }

        // Diagnostic only: SmartScreen decides on it. Self-update never copies this exe, so the mark isn't carried over.
        var exe = Path.Combine(launcher.Folder, launcher.ExeName);
        if (MarkOfTheWeb.IsMarked(exe))
        {
            logger.LogInformation("{Exe} has a Mark of the Web (Zone.Identifier): it was downloaded with a browser", exe);
        }
    }
}
