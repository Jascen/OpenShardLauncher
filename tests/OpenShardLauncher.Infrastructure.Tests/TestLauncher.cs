using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Infrastructure.Http;
using OpenShardLauncher.Shared.Signing;

namespace OpenShardLauncher.Infrastructure.Tests;

// The launcher's infrastructure and workflow services as the client wires them, over a temp launcher folder (data
// folder and install folder), with retry and busy-server delays set to zero. The platform is win-x64 and the game
// launcher is a fake.
internal sealed class TestLauncher : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-infra-launcher-");
    private readonly ServiceProvider _services;

    public const string Platform = "win-x64";

    public TestLauncher(
        Uri updateUrl,
        IReadOnlyList<TrustedKey> keys,
        bool allowUnsignedFeed = false,
        Func<LauncherOptions, LauncherOptions>? configure = null)
    {
        var options = new LauncherOptions
        {
            UpdateUrl = updateUrl.AbsoluteUri,
            TrustedPublicKeys = keys,
            AllowUnsignedFeed = allowUnsignedFeed,
        };
        options = configure?.Invoke(options) ?? options;
        var dataFolder = LauncherDataFolder.Resolve(_root.FullName, "unused", NullLogger.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(new ServerEndpoint(options, serverUrlOverride: null));
        services.AddSingleton(dataFolder);
        services.AddSingleton<FeedStateStore>();
        services.AddSingleton(new PlatformInfo(Platform));
        services.AddSingleton<IGameLauncher>(Game);
        services.AddOpenShardLauncherInfrastructure(o =>
        {
            o.RetryBaseDelay = TimeSpan.Zero;
            o.BusyPause = TimeSpan.Zero;
        });
        services.AddOpenShardLauncherWorkflow();
        _services = services.BuildServiceProvider();

        InstallFolder = new InstallFolder(Path.Combine(_root.FullName, "Game"));
    }

    public InstallFolder InstallFolder { get; }

    public IUpdateServer Server => _services.GetRequiredService<IUpdateServer>();

    public ServerEndpoint Endpoint => _services.GetRequiredService<ServerEndpoint>();

    public TransportPolicy Transport => _services.GetRequiredService<TransportPolicy>();

    public UpdateWorkflow Workflow => _services.GetRequiredService<UpdateWorkflow>();

    public FeedStateStore FeedState => _services.GetRequiredService<FeedStateStore>();

    public FakeGameLauncher Game { get; } = new();

    public InstallSession OpenSession() => Workflow.OpenSession(InstallFolder.Root);

    public void Dispose()
    {
        _services.Dispose();
        _root.Delete(recursive: true);
    }
}

// Says whether TazUO is installed as the test sets it; starting it does nothing.
internal sealed class FakeGameLauncher : IGameLauncher
{
    public bool Installed { get; set; }

    public bool IsInstalled(string installFolder) => Installed;

    public void Start(string installFolder)
    {
    }
}
