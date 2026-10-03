namespace OpenShardLauncher.Client.Presentation;

// Keys into Strings.resx (added in phase 5). The comment says what the {n} arguments are.
public static class StringKeys
{
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

    // Package warnings
    public const string ManifestUnreachableWarning = nameof(ManifestUnreachableWarning);
    public const string ManifestUntrustedWarning = nameof(ManifestUntrustedWarning);
    public const string ManifestUpdatingWarning = nameof(ManifestUpdatingWarning);
    public const string PackagesNotConfiguredWarning = nameof(PackagesNotConfiguredWarning);
    public const string TazUOInstallFailedWarning = nameof(TazUOInstallFailedWarning);
    public const string SelfUpdateFailedWarning = nameof(SelfUpdateFailedWarning);
}
