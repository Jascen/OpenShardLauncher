using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Http;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Tests;

// The launcher's infrastructure services as the client wires them, over a temp launcher folder (data folder and
// install folder), with retry and busy-server delays set to zero.
internal sealed class TestLauncher : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-infra-launcher-");
    private readonly ServiceProvider _services;

    public TestLauncher(Uri updateUrl, IReadOnlyList<TrustedKey> keys, bool allowUnsignedFeed = false)
    {
        var options = new LauncherOptions
        {
            UpdateUrl = updateUrl.AbsoluteUri,
            TrustedPublicKeys = keys,
            AllowUnsignedFeed = allowUnsignedFeed,
        };
        var dataFolder = LauncherDataFolder.Resolve(_root.FullName, "unused", NullLogger.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(new ServerEndpoint(options, serverUrlOverride: null));
        services.AddSingleton(dataFolder);
        services.AddSingleton<FeedStateStore>();
        services.AddOpenShardLauncherInfrastructure(o =>
        {
            o.RetryBaseDelay = TimeSpan.Zero;
            o.BusyPause = TimeSpan.Zero;
        });
        _services = services.BuildServiceProvider();

        InstallFolder = new InstallFolder(Path.Combine(_root.FullName, "Game"));
    }

    public InstallFolder InstallFolder { get; }

    public IUpdateServer Server => _services.GetRequiredService<IUpdateServer>();

    public ServerEndpoint Endpoint => _services.GetRequiredService<ServerEndpoint>();

    public TransportPolicy Transport => _services.GetRequiredService<TransportPolicy>();

    public void Dispose()
    {
        _services.Dispose();
        _root.Delete(recursive: true);
    }
}
