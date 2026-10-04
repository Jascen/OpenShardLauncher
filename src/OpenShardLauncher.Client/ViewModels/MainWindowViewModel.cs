using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Client.ViewModels.Dialogs;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Client.ViewModels;

// The launcher window: composes the progress, status, nav bar, launcher-update banner and security notice view models
// and owns the commands. What the window shows and enables follows from State (LauncherStateMachine) and whether the
// game is installed.
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly UpdateWorkflow _workflow;
    private readonly SettingsService _settings;
    private readonly LauncherOptions _options;
    private readonly LauncherFolder _launcherFolder;
    private readonly IGameLauncher _game;
    private readonly IDialogService _dialogs;
    private readonly Func<SettingsViewModel> _settingsDialog;
    private readonly LauncherSelfUpdateService _selfUpdate;
    private readonly IAppLifetime _lifetime;
    private readonly ILogger<MainWindowViewModel> _logger;
    private InstallSession? _session;

    // Built with no trusted keys and without AllowUnsignedFeed: nothing could ever be verified, so no check runs.
    private readonly bool _noKeys;

    // Whether the last check was one the player asked for (Verify, Retry) rather than the automatic launch check.
    private bool _checkAskedFor;

    public MainWindowViewModel(
        UpdateWorkflow workflow,
        SettingsService settings,
        LauncherOptions options,
        LauncherFolder launcherFolder,
        LauncherDataFolder dataFolder,
        IGameLauncher game,
        IUrlLauncher urls,
        IDialogService dialogs,
        Func<SettingsViewModel> settingsDialog,
        LauncherSelfUpdateService selfUpdate,
        InstalledLauncher launcher,
        LauncherUpdateBannerViewModel launcherUpdate,
        SecurityNoticeViewModel securityNotice,
        IAppLifetime lifetime,
        ILogger<MainWindowViewModel> logger)
    {
        _workflow = workflow;
        _settings = settings;
        _options = options;
        _launcherFolder = launcherFolder;
        _game = game;
        _dialogs = dialogs;
        _settingsDialog = settingsDialog;
        _selfUpdate = selfUpdate;
        _lifetime = lifetime;
        _logger = logger;
        _noKeys = options.TrustedPublicKeys.Count == 0 && !options.AllowUnsignedFeed;
        NavBar = new NavBarViewModel(options.Links, urls, VerifyCommand);
        LauncherUpdate = launcherUpdate;
        SecurityNotice = securityNotice;
        LauncherUpdate.PropertyChanged += OnLauncherUpdateChanged;
        UpdateLauncherCommand.PropertyChanged += OnLauncherUpdateChanged; // IsRunning gates both banner buttons
        Status.Notice = StartupNotice(dataFolder, launcher);
        _settings.ServerEndpoint.Changed += OnServerChanged;
    }

    public string Title => _options.Title;

    public string Subtitle => _options.Subtitle;

    public ProgressViewModel Progress { get; } = new();

    public StatusViewModel Status { get; } = new();

    public NavBarViewModel NavBar { get; }

    public LauncherUpdateBannerViewModel LauncherUpdate { get; }

    public SecurityNoticeViewModel SecurityNotice { get; }

    // The overlay in the window shows Dialogs.Current.
    public IDialogService Dialogs => _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsFailed), nameof(DownloadsReady), nameof(CanPlay), nameof(MainButtonText), nameof(IsMainButtonVisible))]
    [NotifyCanExecuteChangedFor(nameof(MainActionCommand), nameof(VerifyCommand), nameof(RetryCommand), nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateLauncherCommand), nameof(DismissLauncherUpdateCommand))]
    public partial LauncherState State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPlay))]
    [NotifyCanExecuteChangedFor(nameof(MainActionCommand))]
    public partial bool IsGameInstalled { get; private set; }

    public bool IsBusy => State == LauncherState.Working;

    public bool IsFailed => State == LauncherState.Failed;

    public bool DownloadsReady => State == LauncherState.UpdatesReady;

    public bool CanPlay => !IsBusy && !DownloadsReady && IsGameInstalled;

    // The center button downloads waiting updates first, then becomes the play button. Without TazUO it only appears to
    // download updates.
    public string MainButtonText => UiText.Get(DownloadsReady ? StringKeys.DownloadButton : StringKeys.PlayButton);

    public bool IsMainButtonVisible => DownloadsReady || _options.TazUO.Enabled;

    // Every command that runs a check, a download or a launcher update, for Cancel.
    private IEnumerable<IAsyncRelayCommand> RunCommands =>
        [StartCommand, RestartSessionCommand, MainActionCommand, VerifyCommand, RetryCommand, UpdateLauncherCommand];

    public void Dispose()
    {
        _settings.ServerEndpoint.Changed -= OnServerChanged;
        LauncherUpdate.PropertyChanged -= OnLauncherUpdateChanged;
        UpdateLauncherCommand.PropertyChanged -= OnLauncherUpdateChanged;
        _session?.Dispose();
    }

    // Runs once the window is open: opens the install folder and checks it if "verify on launch" is on. A folder that
    // can't be used opens Settings with the reason first.
    [RelayCommand]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var installPath = InstallFolder.ResolvePath(_settings.Current, _options, _launcherFolder.Path);
        if (CheckFolder(installPath) is { } folderError)
        {
            ShowFolderProblem(installPath, folderError);
            await ShowSettingsAsync(folderError);
            return;
        }

        await OpenFolderAsync(installPath, cancellationToken);
    }

    // Starts over after the install folder or the server changed: stops whatever is running, opens a new session and
    // runs the launch check if "verify on launch" is on.
    [RelayCommand]
    private async Task RestartSessionAsync(CancellationToken cancellationToken)
    {
        CancelRuns(except: RestartSessionCommand);
        var installPath = InstallFolder.ResolvePath(_settings.Current, _options, _launcherFolder.Path);
        if (CheckFolder(installPath) is { } folderError)
        {
            CloseSession();
            ShowFolderProblem(installPath, folderError);
            return;
        }

        await OpenFolderAsync(installPath, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunMainAction))]
    private async Task MainActionAsync(CancellationToken cancellationToken)
    {
        if (DownloadsReady)
        {
            await DownloadAsync(cancellationToken);
        }
        else
        {
            await PlayAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private Task VerifyAsync(CancellationToken cancellationToken) => CheckAsync(askedFor: true, cancellationToken);

    // A fresh check, then a download straight away if it found anything. Two runs, so a cancelled download still offers
    // the updates the check found.
    [RelayCommand(CanExecute = nameof(IsFailed))]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        await CheckAsync(askedFor: true, cancellationToken);
        if (DownloadsReady && !cancellationToken.IsCancellationRequested)
        {
            await DownloadAsync(cancellationToken);
        }
    }

    // One Cancel for whichever command is running (the launch check, Verify, Retry or a download).
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => CancelRuns(except: null);

    [RelayCommand]
    private Task OpenSettingsAsync() => ShowSettingsAsync(folderError: null);

    // Downloads the new launcher and hands off to it; this launcher then exits. A running check or download is
    // cancelled first, once the player agrees.
    [RelayCommand(CanExecute = nameof(CanUpdateLauncher))]
    private async Task UpdateLauncherAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            var confirm = new ConfirmViewModel(
                UiText.Get(StringKeys.CancelForLauncherUpdateTitle),
                UiText.Get(StringKeys.CancelForLauncherUpdateMessage),
                UiText.Get(StringKeys.CancelAndUpdateButton),
                UiText.Get(StringKeys.CancelButton));
            if (!await _dialogs.ShowAsync(confirm))
            {
                return;
            }

            await StopRunsAsync();
        }

        if (cancellationToken.IsCancellationRequested || !LauncherUpdate.CanUpdate)
        {
            return;
        }

        var before = (State, Progress: Progress.Save());
        State = LauncherState.Working;
        Status.ShowError(null);
        Progress.Reset(new LocalizedText(StringKeys.UpdatingLauncher));

        SelfUpdateResult result;
        try
        {
            result = await _selfUpdate.UpdateAsync(new Progress<UpdateProgress>(Progress.Report), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result = SelfUpdateResult.Failure(SelfUpdateError.NotOffered);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "The launcher update failed unexpectedly");
            result = SelfUpdateResult.Failure(SelfUpdateError.HandOffFailed);
        }

        if (result.HandedOff)
        {
            _lifetime.Shutdown();
            return;
        }

        State = before.State;
        Progress.Restore(before.Progress);
        if (!cancellationToken.IsCancellationRequested)
        {
            Status.ShowError(ErrorMessageMapper.Map(result.Error!.Value));
        }
    }

    // "Not now": the banner stays hidden until the launcher restarts.
    [RelayCommand(CanExecute = nameof(CanDismissLauncherUpdate))]
    private void DismissLauncherUpdate() => _selfUpdate.Dismiss();

    // Lists what the ignore list kept from being downloaded.
    [RelayCommand(CanExecute = nameof(HasIgnoredItems))]
    private async Task ShowIgnoredAsync()
    {
        var message = UiText.Get(StringKeys.IgnoredListIntro) + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, Status.IgnoredItems);
        await _dialogs.ShowAsync(new MessageViewModel(UiText.Get(StringKeys.IgnoredTitle), message, UiText.Get(StringKeys.OkButton)));
    }

    private bool CanRunMainAction() => DownloadsReady || CanPlay;

    private bool CanVerify() => !IsBusy && _session is not null && !_noKeys;

    private bool CanUpdateLauncher() => LauncherUpdate.CanUpdate && !UpdateLauncherCommand.IsRunning;

    private bool CanDismissLauncherUpdate() => LauncherUpdate.IsVisible && !UpdateLauncherCommand.IsRunning;

    private bool HasIgnoredItems() => Status.HasIgnoredItems;

    private void CancelRuns(IAsyncRelayCommand? except)
    {
        foreach (var command in RunCommands.Where(c => c.IsRunning && c != except))
        {
            command.Cancel();
        }
    }

    // Cancels the check or download that is running and waits until it has wound down.
    private async Task StopRunsAsync()
    {
        var running = RunCommands.Where(c => c.IsRunning && c != UpdateLauncherCommand).ToList();
        CancelRuns(except: UpdateLauncherCommand);
        foreach (var command in running)
        {
            try
            {
#pragma warning disable VSTHRD003 // Waiting for our own command to finish on this thread
                await (command.ExecutionTask ?? Task.CompletedTask);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void OnLauncherUpdateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateLauncherCommand.NotifyCanExecuteChanged();
        DismissLauncherUpdateCommand.NotifyCanExecuteChanged();
    }

    // How the last self-update went (once, right after it), else the data-folder fallback notice.
    private string? StartupNotice(LauncherDataFolder dataFolder, InstalledLauncher launcher)
    {
        var report = UpdateResultMarker.Take(dataFolder.UpdateResultFile, launcher.Version?.ToString() ?? "", _logger);
        return report switch
        {
            { Succeeded: true } => UiText.Get(new LocalizedText(StringKeys.LauncherUpdatedNotice, report.ExpectedVersion)),
            not null => UiText.Get(new LocalizedText(StringKeys.LauncherUpdateFailedNotice, report.ExpectedVersion, report.Error ?? "")),
            null => dataFolder.IsPortable ? null : UiText.Get(StringKeys.DataFolderFallbackNotice),
        };
    }

    // Saving a new install folder or server restarts the session. A new server already did, through
    // ServerEndpoint.Changed, while the dialog saved.
    private async Task ShowSettingsAsync(string? folderError)
    {
        var dialog = _settingsDialog();
        if (folderError is not null)
        {
            dialog.ShowFolderError(folderError);
        }

        if (await _dialogs.ShowAsync(dialog) && IsSessionStale())
        {
            await RestartSessionCommand.ExecuteAsync(null);
        }
        else
        {
            ForgetUnaskedUpdates();
        }
    }

    // Raised on the UI thread by SettingsService.Save.
    private void OnServerChanged(object? sender, EventArgs e)
    {
        _logger.LogInformation("The update server is now {Server}", _settings.ServerEndpoint.Current);
        RestartSessionCommand.Execute(null);
    }

    private bool IsSessionStale() =>
        _session is not { } session
        || !SettingsViewModel.SamePath(session.Folder.Root, InstallFolder.ResolvePath(_settings.Current, _options, _launcherFolder.Path))
        || session.Server != _settings.ServerEndpoint.Current;

    // With "verify on launch" off, updates are only offered when the player asked for the check. So turning it off takes
    // back a download the launch check offered.
    private void ForgetUnaskedUpdates()
    {
        if (_settings.Current.VerifyOnLaunch || _checkAskedFor || State != LauncherState.UpdatesReady)
        {
            return;
        }

        State = LauncherState.Idle;
        Progress.Reset(new LocalizedText(StringKeys.NotVerified));
    }

    private string? CheckFolder(string installPath) =>
        !InstallFolder.IsAllowedLocation(installPath, _launcherFolder.Path) ? StringKeys.InstallFolderNotAllowedError
        : !FolderProbe.IsWritable(installPath) ? StringKeys.InstallFolderNotWritableError
        : null;

    private void ShowFolderProblem(string installPath, string folderError)
    {
        _logger.LogWarning("The install folder {Folder} can't be used: {Error}", installPath, folderError);
        Status.ShowError(new LocalizedText(folderError));
        Progress.Reset(new LocalizedText(StringKeys.NoFolderChosen));
    }

    private async Task OpenFolderAsync(string installPath, CancellationToken cancellationToken)
    {
        OpenSession(installPath);
        if (_noKeys)
        {
            // The game can still be played; it just can't be updated.
            _logger.LogError("The launcher has no trusted keys and unsigned feeds are disabled; no update checks run");
            Status.ShowError(new LocalizedText(StringKeys.NoTrustedKeysError));
            Progress.Reset(new LocalizedText(StringKeys.CheckFailed));
            return;
        }

        if (_settings.Current.VerifyOnLaunch)
        {
            await CheckAsync(askedFor: false, cancellationToken);
        }
    }

    private Task CheckAsync(bool askedFor, CancellationToken cancellationToken)
    {
        _checkAskedFor = askedFor;
        return RunAsync(isDownload: false, (session, progress, _, token) => _workflow.CheckAsync(session, progress, token), cancellationToken);
    }

    private Task DownloadAsync(CancellationToken cancellationToken) =>
        RunAsync(isDownload: true, (session, progress, file, token) => _workflow.DownloadAsync(session, progress, file, token), cancellationToken);

    private async Task RunAsync(
        bool isDownload,
        Func<InstallSession, IProgress<UpdateProgress>, IProgress<FileProgress>, CancellationToken, Task<UpdateOutcome>> run,
        CancellationToken cancellationToken)
    {
        if (_session is not { } session || IsBusy || _noKeys)
        {
            return;
        }

        var started = LauncherStateMachine.Started(isDownload);
        State = started.State;
        Status.ShowError(null);
        SetIgnoredItems([]);
        Progress.Reset(started.Text);

        UpdateOutcome outcome;
        try
        {
            // Session progress runs on this (UI) thread and goes quiet once the session is replaced.
            outcome = await run(session, session.CreateProgress<UpdateProgress>(Progress.Report), session.CreateProgress<FileProgress>(Progress.Report), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            outcome = UpdateOutcome.Cancelled;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "The {Run} failed unexpectedly", isDownload ? "download" : "check");
            outcome = UpdateOutcome.Failure(UpdateError.Unknown);
        }

        if (session == _session)
        {
            ShowOutcome(session, outcome, isDownload);
        }
    }

    private void ShowOutcome(InstallSession session, UpdateOutcome outcome, bool wasDownload)
    {
        var transition = LauncherStateMachine.Completed(outcome, wasDownload);
        switch (outcome.Result)
        {
            case UpdateResult.Finished:
                Progress.Complete(transition.Text);
                break;
            case UpdateResult.UpdatesReady or UpdateResult.PackagesReady:
                Progress.Reset(transition.Text);
                break;
            default:
                Progress.ShowText(transition.Text);
                break;
        }

        Status.ShowError(ErrorMessageMapper.ForOutcome(outcome)
            ?? (outcome.PackageWarnings.Count > 0 ? ErrorMessageMapper.Map(outcome.PackageWarnings[0]) : null));
        SetIgnoredItems(outcome.Result is UpdateResult.Failed or UpdateResult.Cancelled ? [] : outcome.IgnoredItems);
        IsGameInstalled = _game.IsInstalled(session.Folder.Root);
        State = transition.State;
    }

    // Asks first when the files weren't verified and "warn if not verified" is on.
    private async Task PlayAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        if (State != LauncherState.Verified && _settings.Current.WarnIfNotVerified)
        {
            var confirm = new ConfirmViewModel(
                UiText.Get(StringKeys.UnverifiedTitle),
                UiText.Get(StringKeys.UnverifiedMessage),
                UiText.Get(StringKeys.PlayAnywayButton),
                UiText.Get(StringKeys.CancelButton));
            if (!await _dialogs.ShowAsync(confirm) || session != _session)
            {
                return;
            }
        }

        try
        {
            _game.Start(session.Folder.Root);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Could not start the game");
            Status.ShowError(new LocalizedText(StringKeys.LaunchError));
        }
    }

    // A clean slate for a new install folder or server: nothing from the previous session applies.
    private void OpenSession(string installPath)
    {
        _session?.Dispose();
        _session = _workflow.OpenSession(installPath);
        IsGameInstalled = _game.IsInstalled(_session.Folder.Root);
        State = LauncherState.Idle;
        _checkAskedFor = false;
        Status.ShowError(null);
        SetIgnoredItems([]);
        Progress.Reset(new LocalizedText(StringKeys.NotVerified));
        VerifyCommand.NotifyCanExecuteChanged();
    }

    private void CloseSession()
    {
        _session?.Dispose();
        _session = null;
        IsGameInstalled = false;
        State = LauncherState.Idle;
        SetIgnoredItems([]);
        VerifyCommand.NotifyCanExecuteChanged();
    }

    private void SetIgnoredItems(IReadOnlyList<string> items)
    {
        Status.IgnoredItems = items;
        ShowIgnoredCommand.NotifyCanExecuteChanged();
    }
}
