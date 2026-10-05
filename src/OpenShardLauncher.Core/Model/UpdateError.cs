namespace OpenShardLauncher.Core.Model;

// Why a run stopped. Presentation maps these to text; Core has no player-facing strings.
public enum UpdateError
{
    Unknown,
    ConnectionFailed,
    BadData, // The server sent something that couldn't be parsed
    InsecureServer, // Plain http to another machine without "Allow insecure downloads"
    NothingPublished, // files.json is 404: the server has no files published yet
    FeedUntrusted, // files.json has no valid signature from a trusted key (or is a rollback)
    UnsignedFeedNotDefaultServer, // No signature: allowed only from the launcher's default server, not an overridden one
    UnsignedFeedInsecure, // No signature: allowed only over https (or from this machine)
    FeedUpdating, // files.json and its signature still don't match after a refetch, or a blob is missing: mid-upload
    NoTrustedKeys, // The launcher was built with no keys and without AllowUnsignedFeed, so it can never update
    InstallFolderNotWritable,
    DiskFull,
    FileFailed, // A file couldn't be downloaded after the retries; the file name is in UpdateOutcome.FailedFiles
    FileLocked, // A file is open in another program, usually the game
}

// Problems with packages (game client, launcher). They only warn: game files still update.
public enum PackageWarning
{
    ManifestUnreachable,
    ManifestInvalid, // Fetched and trusted, but not a valid manifest
    ManifestUntrusted,
    ManifestUpdating,
    PackagesNotConfigured, // No trusted keys, so packages are disabled
    ClientInstallFailed,
    ClientUnavailable, // Not installed, and the server offers none for this platform
    SelfUpdateFailed,
}

// Thrown across the boundary interfaces when an operation fails for a reason the workflow reports to the player.
public sealed class UpdateException(UpdateError error, string? fileName = null, Exception? inner = null)
    : Exception($"{error}{(fileName is null ? "" : $": {fileName}")}", inner)
{
    public UpdateError Error { get; } = error;

    public string? FileName { get; } = fileName;
}
