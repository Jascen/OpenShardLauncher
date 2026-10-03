using OpenShardLauncher.Core.Model;

namespace OpenShardLauncher.Client.Presentation;

// UpdateError/PackageWarning → the error line under the progress bars.
public static class ErrorMessageMapper
{
    // The error line for a finished run, or null when there's nothing to say.
    public static LocalizedText? ForOutcome(UpdateOutcome outcome)
    {
        if (outcome.Error is { } error)
        {
            return Map(error, outcome.ErrorFile);
        }

        // Locked files first: closing the game and retrying fixes them.
        var failed = outcome.FailedFiles.FirstOrDefault(f => f.Locked) ?? outcome.FailedFiles.FirstOrDefault();
        return failed is null ? null : Map(failed.Locked ? UpdateError.FileLocked : UpdateError.FileFailed, failed.Name);
    }

    public static LocalizedText Map(UpdateError error, string? fileName = null) => error switch
    {
        UpdateError.ConnectionFailed => new(StringKeys.ConnectionFailedError),
        UpdateError.BadData => new(StringKeys.BadDataError),
        UpdateError.InsecureServer => new(StringKeys.InsecureServerError),
        UpdateError.NothingPublished => new(StringKeys.NothingPublishedError),
        UpdateError.FeedUntrusted => new(StringKeys.FeedUntrustedError),
        UpdateError.FeedUpdating => new(StringKeys.FeedUpdatingError),
        UpdateError.NoTrustedKeys => new(StringKeys.NoTrustedKeysError),
        UpdateError.InstallFolderNotWritable => new(StringKeys.InstallFolderNotWritableError),
        UpdateError.DiskFull => new(StringKeys.DiskFullError),
        UpdateError.FileFailed => new(StringKeys.FileFailedError, fileName ?? ""),
        UpdateError.FileLocked => new(StringKeys.FileLockedError, fileName ?? ""),
        _ => new(StringKeys.UnknownError),
    };

    public static LocalizedText Map(PackageWarning warning) => warning switch
    {
        PackageWarning.ManifestUnreachable => new(StringKeys.ManifestUnreachableWarning),
        PackageWarning.ManifestUntrusted => new(StringKeys.ManifestUntrustedWarning),
        PackageWarning.ManifestUpdating => new(StringKeys.ManifestUpdatingWarning),
        PackageWarning.PackagesNotConfigured => new(StringKeys.PackagesNotConfiguredWarning),
        PackageWarning.TazUOInstallFailed => new(StringKeys.TazUOInstallFailedWarning),
        PackageWarning.SelfUpdateFailed => new(StringKeys.SelfUpdateFailedWarning),
        _ => throw new ArgumentOutOfRangeException(nameof(warning), warning, null),
    };
}
