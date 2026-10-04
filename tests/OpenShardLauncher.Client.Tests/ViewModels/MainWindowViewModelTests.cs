using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Client.ViewModels;
using OpenShardLauncher.Client.ViewModels.Dialogs;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Shared.Feed;

namespace OpenShardLauncher.Client.Tests.ViewModels;

// The view model as the app composes it, over a temp launcher folder. The update server can hold the file list until
// the run is cancelled, so a run is in progress for as long as the test needs.
public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly LauncherTestHost _host = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    // One file the install folder doesn't have, so a check offers a download.
    private static FeedResult<FileList> OneFileToDownload =>
        FeedResult<FileList>.Success(new FileList(1, [new FileEntry("data.mul", new string('a', 64), 10)], []), FeedTrust.Unsigned);

    [Fact]
    public async Task Play_and_Verify_cant_execute_while_an_update_is_running()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        _host.Server.Hold = true;

        var launchCheck = viewModel.StartCommand.ExecuteAsync(null);
        await _host.Server.WaitForRequestAsync();

        Assert.Equal(LauncherState.Working, viewModel.State);
        Assert.False(viewModel.MainActionCommand.CanExecute(null)); // Play: the game is installed, but files are updating
        Assert.False(viewModel.VerifyCommand.CanExecute(null));

        viewModel.CancelCommand.Execute(null);
        await launchCheck.WaitAsync(TestToken);

        Assert.Equal(LauncherState.Idle, viewModel.State);
        Assert.True(viewModel.MainActionCommand.CanExecute(null));
        Assert.True(viewModel.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Changing_the_install_folder_restarts_the_session_and_checks_the_new_folder()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        await viewModel.StartCommand.ExecuteAsync(null);
        Assert.Single(_host.Server.Requests);
        var newFolder = Path.Combine(_host.Root, "Elsewhere");

        var settingsClosed = viewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(_host.Dialogs.Current);
        _host.FolderPicker.Next = newFolder;
        await settings.ChangeFolderCommand.ExecuteAsync(null);
        settings.SaveCommand.Execute(null);
        await settingsClosed.WaitAsync(TestToken);

        Assert.Equal(2, _host.Server.Requests.Count);
        Assert.Equal(newFolder, _host.Game.LastCheckedFolder);
        Assert.Equal(newFolder, _host.Settings.Current.InstallPath);
        Assert.Equal(LauncherState.Failed, viewModel.State); // The new check's own result
    }

    [Fact]
    public async Task Changing_the_server_cancels_the_running_check_and_checks_the_new_server()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        _host.Server.Hold = true;
        var launchCheck = viewModel.StartCommand.ExecuteAsync(null);
        await _host.Server.WaitForRequestAsync();
        _host.Server.Hold = false;

        var settingsClosed = viewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(_host.Dialogs.Current);
        settings.ServerUrl = "https://mirror.example.com/feed";
        settings.SaveCommand.Execute(null);
        await settingsClosed.WaitAsync(TestToken);
        await launchCheck.WaitAsync(TestToken);
        await viewModel.RestartSessionCommand.ExecutionTask!.WaitAsync(TestToken);

        Assert.Equal([_host.Get<ServerEndpoint>().Default, new Uri("https://mirror.example.com/feed/")], _host.Server.Requests);
        Assert.Equal(1, _host.Server.CancelledRequests);
        Assert.Equal(LauncherState.Failed, viewModel.State);
    }

    [Fact]
    public async Task Saving_without_changing_the_folder_or_server_keeps_the_session()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        await viewModel.StartCommand.ExecuteAsync(null);

        var settingsClosed = viewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(_host.Dialogs.Current);
        settings.IgnoreList.Text = "Music/";
        settings.SaveCommand.Execute(null);
        await settingsClosed.WaitAsync(TestToken);

        Assert.Single(_host.Server.Requests);
        Assert.Equal(LauncherState.Failed, viewModel.State);
    }

    [Fact]
    public async Task An_unusable_install_folder_opens_settings_with_the_reason_before_checking()
    {
        var host = _host;
        host.Settings.Save(host.Settings.Current with { InstallPath = host.Root }); // Contains the launcher folder
        var viewModel = host.Get<MainWindowViewModel>();

        var start = viewModel.StartCommand.ExecuteAsync(null);

        var settings = Assert.IsType<SettingsViewModel>(host.Dialogs.Current);
        Assert.Equal(UiText.Get(StringKeys.InstallFolderNotAllowedError), settings.FolderError);
        Assert.False(settings.SaveCommand.CanExecute(null));
        Assert.Empty(host.Server.Requests);

        host.FolderPicker.Next = Path.Combine(host.Root, "Game");
        await settings.ChangeFolderCommand.ExecuteAsync(null);
        settings.SaveCommand.Execute(null);
        await start.WaitAsync(TestToken);

        Assert.Single(host.Server.Requests);
        Assert.Equal(Path.Combine(host.Root, "Game"), host.Game.LastCheckedFolder);
    }

    [Fact]
    public async Task Play_asks_first_when_the_files_are_not_verified()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        await viewModel.StartCommand.ExecuteAsync(null); // The check fails: not verified

        var play = viewModel.MainActionCommand.ExecuteAsync(null);
        var confirm = Assert.IsType<ConfirmViewModel>(_host.Dialogs.Current);
        confirm.CancelCommand.Execute(null);
        await play.WaitAsync(TestToken);
        Assert.Equal(0, _host.Game.Starts);

        play = viewModel.MainActionCommand.ExecuteAsync(null);
        Assert.IsType<ConfirmViewModel>(_host.Dialogs.Current).ConfirmCommand.Execute(null);
        await play.WaitAsync(TestToken);
        Assert.Equal(1, _host.Game.Starts);
    }

    [Fact]
    public async Task Turning_verify_on_launch_off_takes_back_updates_the_launch_check_offered()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        _host.Server.Result = OneFileToDownload;
        await viewModel.StartCommand.ExecuteAsync(null);
        Assert.Equal(LauncherState.UpdatesReady, viewModel.State);

        var settingsClosed = viewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(_host.Dialogs.Current);
        settings.VerifyOnLaunch = false;
        settings.SaveCommand.Execute(null);
        await settingsClosed.WaitAsync(TestToken);

        Assert.Equal(LauncherState.Idle, viewModel.State);
        Assert.Equal(UiText.Get(StringKeys.NotVerified), viewModel.Progress.OverallText);
    }

    [Fact]
    public async Task Updates_the_player_asked_for_stay_offered_when_verify_on_launch_is_turned_off()
    {
        var viewModel = _host.Get<MainWindowViewModel>();
        _host.Server.Result = OneFileToDownload;
        await viewModel.StartCommand.ExecuteAsync(null);
        await viewModel.VerifyCommand.ExecuteAsync(null);

        var settingsClosed = viewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(_host.Dialogs.Current);
        settings.VerifyOnLaunch = false;
        settings.SaveCommand.Execute(null);
        await settingsClosed.WaitAsync(TestToken);

        Assert.Equal(LauncherState.UpdatesReady, viewModel.State);
    }

    public void Dispose() => _host.Dispose();
}
