namespace OpenShardLauncher.Core.Model;

public enum UpdateResult
{
    Finished, // Everything checked, downloaded and set up. FailedFiles says whether any file failed
    UpdatesReady, // Files differ from the server or need removing, waiting for a download
    PackagesReady, // Files match but the TazUO launcher is out of date or missing, waiting for a download
    Failed, // Stopped by an error, see UpdateOutcome.Error
    Cancelled,
}

public sealed record FailedFile(string Name, bool Locked);

// The immutable result of one check or download. Replaces reading state back off a service after the run.
public sealed record UpdateOutcome
{
    public required UpdateResult Result { get; init; }

    public UpdateError? Error { get; init; }

    // The file the error is about, for FileFailed/FileLocked.
    public string? ErrorFile { get; init; }

    public IReadOnlyList<FailedFile> FailedFiles { get; init; } = [];

    // Sorted; a folder is listed once with a trailing '/'.
    public IReadOnlyList<string> IgnoredItems { get; init; } = [];

    public IReadOnlyList<PackageWarning> PackageWarnings { get; init; } = [];

    // What a check found waiting (zero after a download).
    public int PendingDownloads { get; init; }

    public int PendingRemovals { get; init; }

    // The file list was accepted without a signature (AllowUnsignedFeed).
    public bool FeedUnsigned { get; init; }

    public static UpdateOutcome Cancelled { get; } = new() { Result = UpdateResult.Cancelled };

    public static UpdateOutcome Failure(UpdateError error, string? file = null) =>
        new() { Result = UpdateResult.Failed, Error = error, ErrorFile = file };
}
