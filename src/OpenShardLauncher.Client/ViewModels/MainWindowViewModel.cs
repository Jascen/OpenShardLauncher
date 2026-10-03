using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Core.Abstractions;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Core.Workflow;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Client.ViewModels;

// The launcher window: composes the progress, status and nav bar view models and owns the commands. What the window
// shows and enables follows from State (LauncherStateMachine) and whether the game is installed.
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly UpdateWorkflow _workflow;
    private readonly SettingsService _settings;
    private readonly LauncherOptions _options;
    private readonly LauncherFolder _launcherFolder;
    private readonly IGameLauncher _game;
    private readonly ILogger<MainWindowViewModel> _logger;
    private InstallSession? _session;

    public MainWindowViewModel(
        UpdateWorkflow workflow,
        SettingsService settings,
        LauncherOptions options,
        LauncherFolder launcherFolder,
        LauncherDataFolder dataFolder,
        IGameLauncher game,
        IUrlLauncher urls,
        ILogger<MainWindowViewModel> logger)
    {
        _workflow = workflow;
        _settings = settings;
        _options = options;
        _launcherFolder = launcherFolder;
        _game = game;
        _logger = logger;
        NavBar = new NavBarViewModel(options.Links, urls, VerifyCommand);
        Status.Notice = dataFolder.IsPortable ? null : UiText.Get(StringKeys.DataFolderFallbackNotice);
    }

    public string Title => _options.Title;

    public string Subtitle => _options.Subtitle;

    public ProgressViewModel Progress { get; } = new();

    public StatusViewModel Status { get; } = new();

    public NavBarViewModel NavBar { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsFailed), nameof(DownloadsReady), nameof(CanPlay), nameof(MainButtonText), nameof(IsMainButtonVisible))]
    [NotifyCanExecuteChangedFor(nameof(MainActionCommand), nameof(VerifyCommand), nameof(RetryCommand), nameof(CancelCommand))]
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

    // Every command that runs a check or download, for Cancel.
    private IEnumerable<IAsyncRelayCommand> RunCommands => [StartCommand, MainActionCommand, VerifyCommand, RetryCommand];

    public void Dispose() => _session?.Dispose();

    // Runs once the window is open: opens the install folder and checks it if "verify on launch" is on.
    [RelayCommand]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var installPath = InstallFolder.ResolvePath(_settings.Current, _options, _launcherFolder.Path);
        var folderError = !InstallFolder.IsAllowedLocation(installPath, _launcherFolder.Path) ? StringKeys.InstallFolderNotAllowedError
            : !FolderProbe.IsWritable(installPath) ? StringKeys.InstallFolderNotWritableError
            : null;
        if (folderError is not null)
        {
            // Phase 6 opens Settings with this error.
            _logger.LogWarning("The install folder {Folder} can't be used: {Error}", installPath, folderError);
            Status.ShowError(new LocalizedText(folderError));
            Progress.Reset(new LocalizedText(StringKeys.NoFolderChosen));
            return;
        }

        OpenSession(installPath);
        if (_settings.Current.VerifyOnLaunch)
        {
            await CheckAsync(cancellationToken);
        }
        else
        {
            Progress.Reset(new LocalizedText(StringKeys.NotVerified));
        }
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
            Play();
        }
    }

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private Task VerifyAsync(CancellationToken cancellationToken) => CheckAsync(cancellationToken);

    // A fresh check, then a download straight away if it found anything. Two runs, so a cancelled download still offers
    // the updates the check found.
    [RelayCommand(CanExecute = nameof(IsFailed))]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        await CheckAsync(cancellationToken);
        if (DownloadsReady && !cancellationToken.IsCancellationRequested)
        {
            await DownloadAsync(cancellationToken);
        }
    }

    // One Cancel for whichever command is running (the launch check, Verify, Retry or a download).
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        foreach (var command in RunCommands.Where(c => c.IsRunning))
        {
            command.Cancel();
        }
    }

    // Phase 6 opens the settings dialog.
    [RelayCommand]
    private Task OpenSettingsAsync() => Task.CompletedTask;

    // Phase 6 lists the ignored items in a message dialog.
    [RelayCommand(CanExecute = nameof(HasIgnoredItems))]
    private Task ShowIgnoredAsync() => Task.CompletedTask;

    private bool CanRunMainAction() => DownloadsReady || CanPlay;

    private bool CanVerify() => !IsBusy && _session is not null;

    private bool HasIgnoredItems() => Status.HasIgnoredItems;

    private Task CheckAsync(CancellationToken cancellationToken) =>
        RunAsync(isDownload: false, (session, progress, _, token) => _workflow.CheckAsync(session, progress, token), cancellationToken);

    private Task DownloadAsync(CancellationToken cancellationToken) =>
        RunAsync(isDownload: true, (session, progress, file, token) => _workflow.DownloadAsync(session, progress, file, token), cancellationToken);

    private async Task RunAsync(
        bool isDownload,
        Func<InstallSession, IProgress<UpdateProgress>, IProgress<FileProgress>, CancellationToken, Task<UpdateOutcome>> run,
        CancellationToken cancellationToken)
    {
        if (_session is not { } session || IsBusy)
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

    private void Play()
    {
        // Phase 6 asks first when the files aren't verified and "warn if not verified" is on.
        if (_session is not { } session)
        {
            return;
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

    // A clean slate for a new install folder: nothing from a previous one applies.
    private void OpenSession(string installPath)
    {
        _session?.Dispose();
        _session = _workflow.OpenSession(installPath);
        IsGameInstalled = _game.IsInstalled(_session.Folder.Root);
        State = LauncherState.Idle;
        Status.ShowError(null);
        SetIgnoredItems([]);
        Progress.Reset(new LocalizedText(StringKeys.NotVerified));
        VerifyCommand.NotifyCanExecuteChanged();
    }

    private void SetIgnoredItems(IReadOnlyList<string> items)
    {
        Status.IgnoredItems = items;
        ShowIgnoredCommand.NotifyCanExecuteChanged();
    }
}
