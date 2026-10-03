namespace OpenShardLauncher.Client.Presentation;

// Keys into Resources/Strings.resx. The comment says what the {n} arguments are.
public static class StringKeys
{
    // Main window
    public const string PlayButton = nameof(PlayButton);
    public const string DownloadButton = nameof(DownloadButton);
    public const string RetryButton = nameof(RetryButton);
    public const string ShowIgnoredButton = nameof(ShowIgnoredButton);
    public const string IgnoredSkipped = nameof(IgnoredSkipped); // {0} = number of files and folders
    public const string SettingsTooltip = nameof(SettingsTooltip);
    public const string MinimizeTooltip = nameof(MinimizeTooltip);
    public const string CloseTooltip = nameof(CloseTooltip);
    public const string CancelTooltip = nameof(CancelTooltip);
    public const string DataFolderFallbackNotice = nameof(DataFolderFallbackNotice);

    // Status line
    public const string CheckingForUpdates = nameof(CheckingForUpdates);
    public const string StartingDownload = nameof(StartingDownload);
    public const string UpdatesReady = nameof(UpdatesReady);
    public const string RemovalsReady = nameof(RemovalsReady); // Only files to remove: a removal-only publish
    public const string PackagesReady = nameof(PackagesReady);
    public const string Finished = nameof(Finished);
    public const string FinishedWithFailures = nameof(FinishedWithFailures); // {0} = number of files
    public const string CheckFailed = nameof(CheckFailed);
    public const string DownloadFailed = nameof(DownloadFailed);
    public const string Cancelled = nameof(Cancelled);
    public const string NotVerified = nameof(NotVerified); // Verify on launch is off
    public const string NoFolderChosen = nameof(NoFolderChosen); // The install folder can't be used

    // Progress
    public const string FetchingFileList = nameof(FetchingFileList);
    public const string ComparingFiles = nameof(ComparingFiles); // {0} = files done, {1} = total
    public const string DownloadingFiles = nameof(DownloadingFiles); // {0} = files done, {1} = total, {2} = speed
    public const string DownloadingBytes = nameof(DownloadingBytes); // {0} = done, {1} = total size, {2} = speed, {3} = time left
    public const string RemovingFiles = nameof(RemovingFiles); // {0} = files done, {1} = total
    public const string InstallingTazUO = nameof(InstallingTazUO);
    public const string DownloadingTazUO = nameof(DownloadingTazUO); // {0} = done, {1} = total size
    public const string UpdatingLauncher = nameof(UpdatingLauncher);
    public const string DownloadingLauncher = nameof(DownloadingLauncher); // {0} = done, {1} = total size
    public const string CurrentFile = nameof(CurrentFile); // {0} = file name

    // Errors
    public const string UnknownError = nameof(UnknownError);
    public const string ConnectionFailedError = nameof(ConnectionFailedError);
    public const string BadDataError = nameof(BadDataError);
    public const string InsecureServerError = nameof(InsecureServerError);
    public const string NothingPublishedError = nameof(NothingPublishedError);
    public const string FeedUntrustedError = nameof(FeedUntrustedError);
    public const string FeedUpdatingError = nameof(FeedUpdatingError);
    public const string NoTrustedKeysError = nameof(NoTrustedKeysError);
    public const string InstallFolderNotWritableError = nameof(InstallFolderNotWritableError);
    public const string DiskFullError = nameof(DiskFullError);
    public const string FileFailedError = nameof(FileFailedError); // {0} = file name
    public const string FileLockedError = nameof(FileLockedError); // {0} = file name
    public const string InstallFolderNotAllowedError = nameof(InstallFolderNotAllowedError);
    public const string LaunchError = nameof(LaunchError);

    // Package warnings
    public const string ManifestUnreachableWarning = nameof(ManifestUnreachableWarning);
    public const string ManifestUntrustedWarning = nameof(ManifestUntrustedWarning);
    public const string ManifestUpdatingWarning = nameof(ManifestUpdatingWarning);
    public const string PackagesNotConfiguredWarning = nameof(PackagesNotConfiguredWarning);
    public const string TazUOInstallFailedWarning = nameof(TazUOInstallFailedWarning);
    public const string SelfUpdateFailedWarning = nameof(SelfUpdateFailedWarning);
}
