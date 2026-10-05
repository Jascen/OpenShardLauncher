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
        Func<LauncherOptions, LauncherOptions>? configure = null,
        string launcherVersion = "1.0.0")
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
        LauncherFolder = Path.Combine(_root.FullName, "launcher");
        Directory.CreateDirectory(LauncherFolder);
        services.AddSingleton(new InstalledLauncher(Version.TryParse(launcherVersion, out var version) ? version : null, LauncherFolder, ExeName));
        services.AddSingleton<ISelfUpdater>(SelfUpdater);
        services.AddOpenShardLauncherInfrastructure(o =>
        {
            o.RetryBaseDelay = TimeSpan.Zero;
            o.BusyPause = TimeSpan.Zero;
        });
        services.AddOpenShardLauncherWorkflow();
        _services = services.BuildServiceProvider();

        InstallFolder = new InstallFolder(Path.Combine(_root.FullName, "Game"));
    }

    public const string ExeName = "Launcher.exe";

    public InstallFolder InstallFolder { get; }

    // The running launcher's folder (InstalledLauncher.Folder), where .temp/ goes.
    public string LauncherFolder { get; }

    public LauncherDataFolder DataFolder => _services.GetRequiredService<LauncherDataFolder>();

    public FakeSelfUpdater SelfUpdater { get; } = new();

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

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

// Says whether the client is installed as the test sets it; starting it does nothing.
internal sealed class FakeGameLauncher : IGameLauncher
{
    public bool Installed { get; set; }

    public bool IsInstalled(string installFolder) => Installed;

    public void Start(string installFolder)
    {
    }
}

// Records the hand-off instead of starting a process.
internal sealed class FakeSelfUpdater : ISelfUpdater
{
    public bool Succeeds { get; set; } = true;

    public (string StagingFolder, string Version)? HandedOff { get; private set; }

    public Task<bool> HandOffAsync(string stagingFolder, string newVersion, CancellationToken cancellationToken)
    {
        HandedOff = (stagingFolder, newVersion);
        return Task.FromResult(Succeeds);
    }
}
