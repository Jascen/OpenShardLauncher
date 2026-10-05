using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenShardLauncher.Client.Presentation;
using OpenShardLauncher.Client.Services;
using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Storage;
using OpenShardLauncher.Infrastructure.Http;
using OpenShardLauncher.Infrastructure.Platform;

namespace OpenShardLauncher.Client.ViewModels.Dialogs;

// Edits a copy of the player's settings, so Cancel changes nothing. Save applies them through SettingsService (which
// updates the server and the transport rule) and writes the ignore list; the main window restarts the session when
// the install folder or the server changed. The result is true when saved.
public sealed partial class SettingsViewModel : DialogViewModelBase<bool>
{
    private readonly SettingsService _settings;
    private readonly LauncherOptions _options;
    private readonly LauncherFolder _launcherFolder;
    private readonly IFolderPicker _folderPicker;

    public SettingsViewModel(
        SettingsService settings,
        LauncherOptions options,
        LauncherFolder launcherFolder,
        IFolderPicker folderPicker,
        SecurityNoticeViewModel securityNotice,
        ILogger<SettingsViewModel> logger)
    {
        SecurityNotice = securityNotice;
        _settings = settings;
        _options = options;
        _launcherFolder = launcherFolder;
        _folderPicker = folderPicker;

        var current = settings.Current;
        InstallPath = InstallFolder.ResolvePath(current, options, launcherFolder.Path);
        VerifyOnLaunch = current.VerifyOnLaunch;
        WarnIfNotVerified = current.WarnIfNotVerified;
        AllowInsecureDownloads = current.AllowInsecureDownloads;
        ServerUrl = settings.ServerEndpoint.Current.AbsoluteUri;
        IgnoreList = new IgnoreListEditorViewModel(logger);
        IgnoreList.Load(InstallPath);
    }

    public override string Title => UiText.Get(StringKeys.SettingsTitle);

    public IgnoreListEditorViewModel IgnoreList { get; }

    // The unsigned-feed notice as a read-only line; hidden unless the launcher allows unsigned feeds.
    public SecurityNoticeViewModel SecurityNotice { get; }

    // Only meaningful with a client for Play to start.
    public bool ShowWarnIfNotVerified => _options.Client.Enabled;

    // The placeholder: what an empty box means.
    public string DefaultServerUrl => _settings.ServerEndpoint.Default.AbsoluteUri;

    [ObservableProperty]
    public partial string InstallPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderError))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? FolderError { get; private set; }

    [ObservableProperty]
    public partial bool VerifyOnLaunch { get; set; }

    [ObservableProperty]
    public partial bool WarnIfNotVerified { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerUrlError), nameof(HasServerUrlError), nameof(ShowMirrorNote))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool AllowInsecureDownloads { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerUrlError), nameof(HasServerUrlError), nameof(IsDefaultServer), nameof(ShowMirrorNote))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(ResetServerUrlCommand))]
    public partial string ServerUrl { get; set; }

    public bool HasFolderError => FolderError is not null;

    public string? ServerUrlError => ValidateServerUrl(ServerUrl, AllowInsecureDownloads) is { } key ? UiText.Get(key) : null;

    public bool HasServerUrlError => ServerUrlError is not null;

    // An empty box means the default too.
    public bool IsDefaultServer =>
        string.IsNullOrWhiteSpace(ServerUrl)
        || (ServerEndpoint.TryNormalize(ServerUrl, out var url) && url == _settings.ServerEndpoint.Default);

    public bool ShowMirrorNote => !IsDefaultServer && !HasServerUrlError;

    // The error's string key, or null when the address can be saved. Blank means the default. Otherwise: an absolute
    // http/https address with no user info, query or fragment. Plain http only to this machine, or anywhere while
    // "Allow insecure downloads" is on (SecureTransportHandler enforces the same rule on every request).
    public static string? ValidateServerUrl(string? url, bool allowInsecureDownloads)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!ServerEndpoint.TryNormalize(url, out var uri))
        {
            return StringKeys.ServerUrlInvalidError;
        }

        return TransportPolicy.IsSecure(uri) || allowInsecureDownloads ? null : StringKeys.ServerUrlInsecureError;
    }

    // Shows why the folder can't be used, e.g. when the default folder isn't writable on first launch.
    public void ShowFolderError(string key) => FolderError = UiText.Get(key);

    // A refused folder leaves the current one in place, with the reason underneath.
    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        var title = UiText.Get(new LocalizedText(StringKeys.ChooseFolderTitle, _options.Title));
        var folder = await _folderPicker.PickFolderAsync(title, InstallPath);
        if (folder is null)
        {
            return;
        }

        if (CheckFolder(folder) is { } error)
        {
            ShowFolderError(error);
            return;
        }

        FolderError = null;
        InstallPath = Path.GetFullPath(folder);
        IgnoreList.Load(InstallPath);
    }

    [RelayCommand(CanExecute = nameof(CanResetServerUrl))]
    private void ResetServerUrl() => ServerUrl = DefaultServerUrl;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // The ignore list first, so a check started by the save already uses it.
        IgnoreList.SaveIfChanged();
        _settings.Save(ToSettings());
        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);

    private bool CanResetServerUrl() => !IsDefaultServer;

    private bool CanSave() => !HasFolderError && !HasServerUrlError;

    // The folder must be writable and can't be the launcher's own folder or contain it, so game files never overwrite
    // or sit beside the launcher's binaries. A subfolder such as the default Game/ is fine.
    private string? CheckFolder(string folder) =>
        !InstallFolder.IsAllowedLocation(folder, _launcherFolder.Path) ? StringKeys.InstallFolderNotAllowedError
        : !FolderProbe.IsWritable(folder) ? StringKeys.InstallFolderNotWritableError
        : null;

    // The default folder and server are stored as null, so players who never changed them follow a later launcher's
    // defaults.
    private UserSettings ToSettings()
    {
        var defaultPath = InstallFolder.ResolvePath(new UserSettings(), _options, _launcherFolder.Path);
        return _settings.Current with
        {
            InstallPath = SamePath(InstallPath, defaultPath) ? null : InstallPath,
            VerifyOnLaunch = VerifyOnLaunch,
            WarnIfNotVerified = WarnIfNotVerified,
            AllowInsecureDownloads = AllowInsecureDownloads,
            ServerUrlOverride = IsDefaultServer ? null : ServerEndpoint.Normalize(new Uri(ServerUrl.Trim())).AbsoluteUri,
        };
    }

    internal static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
