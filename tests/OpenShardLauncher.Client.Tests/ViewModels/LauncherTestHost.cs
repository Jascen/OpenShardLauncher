using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Client.Composition;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Client.ViewModels;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Client.Tests.ViewModels;

// The launcher's services as the app composes them, over a temp launcher folder, with the network, the game and the
// folder picker replaced. Dialogs go through the real DialogService, so tests answer them through Dialogs.Current.
internal sealed class LauncherTestHost : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-client-");
    private readonly ServiceProvider _services;

    public LauncherTestHost(LauncherOptions? options = null, UserSettings? settings = null)
    {
        LauncherFolder = Path.Combine(_root.FullName, "launcher");
        Directory.CreateDirectory(LauncherFolder);
        var dataFolder = LauncherDataFolder.Resolve(LauncherFolder, "unused", NullLogger.Instance);
        if (settings is not null)
        {
            new SettingsStore(dataFolder, NullLogger<SettingsStore>.Instance).Save(settings);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLauncher(options ?? new LauncherOptions { AllowUnsignedFeed = true }, dataFolder, new LauncherFolder(LauncherFolder));
        services.AddSingleton<IUpdateServer>(sp => new FakeUpdateServer(sp.GetRequiredService<ServerEndpoint>()));
        services.AddSingleton<IGameLauncher>(Game);
        services.AddSingleton<IFolderPicker>(FolderPicker);
        services.AddSingleton(new InstalledLauncher(new Version(1, 0, 0), LauncherFolder, ExeName));
        services.AddSingleton<ISelfUpdater>(SelfUpdater);
        services.AddSingleton<IAppLifetime>(Lifetime);
        services.AddSingleton(sp => new LauncherUpdateBannerViewModel(
            sp.GetRequiredService<LauncherSelfUpdateService>(), sp.GetRequiredService<InstalledLauncher>(), dataFolder, _ => LauncherFolderWritable));
        _services = services.BuildServiceProvider();
    }

    public const string ExeName = "Launcher.exe";

    public LauncherDataFolder DataFolder => Get<LauncherDataFolder>();

    public FakeSelfUpdater SelfUpdater { get; } = new();

    public FakeAppLifetime Lifetime { get; } = new();

    // What the banner's writability probe of the launcher folder answers.
    public bool LauncherFolderWritable { get; set; } = true;

    // Folders the test can use as install folders: outside the launcher folder.
    public string Root => _root.FullName;

    public string LauncherFolder { get; }

    public FakeGame Game { get; } = new();

    public FakeFolderPicker FolderPicker { get; } = new();

    public FakeUpdateServer Server => (FakeUpdateServer)Get<IUpdateServer>();

    public IDialogService Dialogs => Get<IDialogService>();

    public SettingsService Settings => Get<SettingsService>();

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    public void Dispose()
    {
        _services.Dispose();
        _root.Delete(recursive: true);
    }
}

// Answers the file list with Result, or holds it until cancelled while Hold is on. Records which server each request
// went to, as the real client reads ServerEndpoint.Current per request.
internal sealed class FakeUpdateServer(ServerEndpoint endpoint) : IUpdateServer
{
    private readonly SemaphoreSlim _requested = new(0);
    private readonly Lock _lock = new();
    private readonly List<Uri> _requests = [];

    public volatile bool Hold;

    public FeedResult<FileList> Result { get; set; } = FeedResult<FileList>.Failure(UpdateError.ConnectionFailed);

    public int CancelledRequests;

    public IReadOnlyList<Uri> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public Task WaitForRequestAsync() => _requested.WaitAsync(TestContext.Current.CancellationToken);

    public async Task<FeedResult<FileList>> GetFileListAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _requests.Add(endpoint.Current);
        }

        _requested.Release();
        if (!Hold)
        {
            return Result;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref CancelledRequests);
            throw;
        }

        return Result;
    }

    public FeedResult<PackageManifest> Manifest { get; set; } = FeedResult<PackageManifest>.Failure(UpdateError.NothingPublished);

    // Package files by name, for DownloadPackageAsync.
    public Dictionary<string, byte[]> Packages { get; } = [];

    public Task<FeedResult<PackageManifest>> GetPackageManifestAsync(CancellationToken cancellationToken) => Task.FromResult(Manifest);

    public Task DownloadBlobAsync(FileEntry file, InstallFolder folder, IProgress<long>? progress, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    // Yields first, like a real download, so the command calling it is still running when it fails.
    public async Task DownloadPackageAsync(PackageEntry package, string destinationPath, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        await Task.Yield();
        await File.WriteAllBytesAsync(
            destinationPath,
            Packages.TryGetValue(package.File, out var bytes) ? bytes : throw new UpdateException(UpdateError.FileFailed, package.File),
            cancellationToken);
    }
}

internal sealed class FakeGame : IGameLauncher
{
    public string? LastCheckedFolder { get; private set; }

    public int Starts { get; private set; }

    public bool IsInstalled(string installFolder)
    {
        LastCheckedFolder = installFolder;
        return true;
    }

    public void Start(string installFolder) => Starts++;
}

internal sealed class FakeFolderPicker : IFolderPicker
{
    // What the next pick returns; null is a cancelled picker.
    public string? Next { get; set; }

    public Task<string?> PickFolderAsync(string title, string? startFolder) => Task.FromResult(Next);
}

internal sealed class FakeSelfUpdater : ISelfUpdater
{
    public string? HandedOffVersion { get; private set; }

    public Task<bool> HandOffAsync(string stagingFolder, string newVersion, CancellationToken cancellationToken)
    {
        HandedOffVersion = newVersion;
        return Task.FromResult(true);
    }
}

internal sealed class FakeAppLifetime : IAppLifetime
{
    public int Shutdowns { get; private set; }

    public void Shutdown() => Shutdowns++;
}
