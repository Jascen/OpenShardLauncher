using OpenShardLauncher.Core.Model;
using OpenShardLauncher.Core.Packages;

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
        UpdateError.UnsignedFeedNotDefaultServer => new(StringKeys.UnsignedFeedNotDefaultServerError),
        UpdateError.UnsignedFeedInsecure => new(StringKeys.UnsignedFeedInsecureError),
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
        PackageWarning.ManifestInvalid => new(StringKeys.ManifestInvalidWarning),
        PackageWarning.ManifestUntrusted => new(StringKeys.ManifestUntrustedWarning),
        PackageWarning.ManifestUpdating => new(StringKeys.ManifestUpdatingWarning),
        PackageWarning.PackagesNotConfigured => new(StringKeys.PackagesNotConfiguredWarning),
        PackageWarning.ClientInstallFailed => new(StringKeys.ClientInstallFailedWarning),
        PackageWarning.ClientUnavailable => new(StringKeys.ClientUnavailableWarning),
        PackageWarning.SelfUpdateFailed => new(StringKeys.SelfUpdateFailedWarning),
        _ => throw new ArgumentOutOfRangeException(nameof(warning), warning, null),
    };

    public static LocalizedText Map(SelfUpdateError error) => error switch
    {
        SelfUpdateError.NotOffered => new(StringKeys.SelfUpdateNotOfferedError),
        SelfUpdateError.DownloadFailed => new(StringKeys.SelfUpdateDownloadFailedError),
        SelfUpdateError.VerificationFailed => new(StringKeys.SelfUpdateVerificationFailedError),
        SelfUpdateError.PackageInvalid => new(StringKeys.SelfUpdatePackageInvalidError),
        SelfUpdateError.HandOffFailed => new(StringKeys.SelfUpdateHandOffFailedError),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}
