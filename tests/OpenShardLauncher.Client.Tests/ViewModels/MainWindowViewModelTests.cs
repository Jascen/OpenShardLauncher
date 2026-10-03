using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenShardLauncher.Client.Composition;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Client.ViewModels;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Client.Tests.ViewModels;

// The view model as the app composes it, over a temp launcher folder. The update server holds the file list until the
// test releases it, so the run is in progress for as long as the test needs.
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("osl-client-");
    private readonly HeldUpdateServer _server = new();
    private readonly ServiceProvider _services;

    public MainWindowViewModelTests()
    {
        var options = new LauncherOptions { AllowUnsignedFeed = true };
        var dataFolder = LauncherDataFolder.Resolve(_root.FullName, "unused", NullLogger.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLauncher(options, dataFolder, new LauncherFolder(_root.FullName));
        services.AddSingleton<IUpdateServer>(_server);
        services.AddSingleton<IGameLauncher>(new InstalledGame());
        _services = services.BuildServiceProvider();
    }

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Play_and_Verify_cant_execute_while_an_update_is_running()
    {
        var viewModel = _services.GetRequiredService<MainWindowViewModel>();

        var launchCheck = viewModel.StartCommand.ExecuteAsync(null);
        await _server.FileListRequested.Task.WaitAsync(TestToken);

        Assert.Equal(LauncherState.Working, viewModel.State);
        Assert.False(viewModel.MainActionCommand.CanExecute(null)); // Play: the game is installed, but files are updating
        Assert.False(viewModel.VerifyCommand.CanExecute(null));

        _server.Release(FeedResult<FileList>.Failure(UpdateError.ConnectionFailed));
        await launchCheck.WaitAsync(TestToken);

        Assert.Equal(LauncherState.Failed, viewModel.State);
        Assert.True(viewModel.MainActionCommand.CanExecute(null));
        Assert.True(viewModel.VerifyCommand.CanExecute(null));
    }

    public void Dispose()
    {
        _services.Dispose();
        _root.Delete(recursive: true);
    }

    // Answers the file list only when the test releases it; nothing else is reached in these tests.
    private sealed class HeldUpdateServer : IUpdateServer
    {
        private readonly TaskCompletionSource<FeedResult<FileList>> _fileList = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FileListRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(FeedResult<FileList> result) => _fileList.SetResult(result);

        public Task<FeedResult<FileList>> GetFileListAsync(CancellationToken cancellationToken)
        {
            FileListRequested.TrySetResult();
            return _fileList.Task.WaitAsync(cancellationToken);
        }

        public Task<FeedResult<PackageManifest>> GetPackageManifestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(FeedResult<PackageManifest>.Failure(UpdateError.NothingPublished));

        public Task DownloadBlobAsync(FileEntry file, InstallFolder folder, IProgress<long>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DownloadPackageAsync(PackageEntry package, string destinationPath, IProgress<long>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InstalledGame : IGameLauncher
    {
        public bool IsInstalled(string installFolder) => true;

        public void Start(string installFolder)
        {
        }
    }
}
