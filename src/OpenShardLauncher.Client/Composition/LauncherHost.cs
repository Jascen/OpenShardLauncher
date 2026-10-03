using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Core.Model;
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
        LogStartup(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(LauncherHost)), options, dataFolder);
        return host;
    }

    private static void LogStartup(ILogger logger, LauncherOptions options, LauncherDataFolder dataFolder)
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
    }
}
